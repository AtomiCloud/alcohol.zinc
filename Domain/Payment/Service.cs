using CSharp_Result;
using Domain.Exceptions;
using NodaMoney;

namespace Domain.Payment;

public class PaymentService(
  IPaymentCustomerRepository repo,
  IPaymentGateway gateway
) : IPaymentService
{
  // 1. CreateCustomerAsync - 3-step check: DB -> Airwallex -> Create
  public Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId)
  {
    return repo.GetByUserId(userId)
      .ThenAwait(dbCustomer =>
      {
        // Step 1: Check if exists in DB
        if (dbCustomer != null)
          return dbCustomer.Principal.ToAsyncResult();

        // Step 2: Check if exists in Airwallex by merchant_customer_id
        return gateway.GetCustomerIdByMerchantIdAsync(userId)
          .ThenAwait(airwallexCustomerId =>
          {
            // If found in Airwallex, save to DB
            if (airwallexCustomerId != null)
              return repo.Create(userId, airwallexCustomerId);

            // Step 3: Create new customer in Airwallex, then save to DB
            return gateway.CreateCustomerAsync(userId)
              .ThenAwait(newCustomerId => repo.Create(userId, newCustomerId));
          });
      });
  }

  // 2. GenerateClientSecretAsync - Get client secret for frontend
  public Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer),userId)
          : customer.ToResult()
      )
      .ThenAwait(customer =>
        gateway.GenerateClientSecretAsync(customer.Principal.Record.AirwallexCustomerId)
          .Then(clientSecret => new ClientSecretResult
          {
            ClientSecret = clientSecret,
            CustomerId = customer.Principal.Record.AirwallexCustomerId
          }, Errors.MapNone)
      );
  }

  // 4. GetCustomerByUserId - Return customer with PaymentConsentId field
  public Task<Result<PaymentCustomer?>> GetCustomerByUserId(string userId)
  {
    return repo.GetByUserId(userId);
  }

  // 5. SearchCustomers - Query customers
  public Task<Result<IEnumerable<PaymentCustomerPrincipal>>> SearchCustomers(PaymentCustomerSearch search)
  {
    return repo.Search(search);
  }

  // Update payment consent from webhook; the purpose is derived from the
  // consent's merchant_trigger_reason (scheduled -> Subscription).
  public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
    string airwallexCustomerId,
    string? paymentConsentId,
    PaymentConsentStatus? consentStatus,
    ConsentPurpose purpose)
  {
    return repo.UpdatePaymentConsentByAirwallexCustomerId(
      airwallexCustomerId,
      paymentConsentId,
      consentStatus,
      purpose);
  }

  public Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .Then(customer => new PaymentConsentStatusResult
      {
        ConsentId = customer.Principal.Record.ConsentIdFor(purpose),
        Status = customer.Principal.Record.ConsentStatusFor(purpose),
        HasPaymentConsent = customer.Principal.Record.HasConsentFor(purpose)
      }, Errors.MapNone);
  }

  public Task<Result<bool>> HasPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    return repo.GetByUserId(userId)
      .Then(customer => customer?.Principal.Record.HasConsentFor(purpose) ?? false, Errors.MapNone);
  }

  public Task<Result<Unit>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .Then(customer =>
      {
        if (string.IsNullOrEmpty(customer.Principal.Record.ConsentIdFor(purpose)))
        {
          return new NotFoundException($"No {purpose} payment consent found for userId: {userId}", typeof(PaymentCustomer), userId);
        }
        return customer.ToResult();
      })
      .ThenAwait(customer =>
        gateway.DisablePaymentConsentAsync(customer.Principal.Record.ConsentIdFor(purpose)!)
          .ThenAwait(_ => repo.DisablePaymentConsentAsync(userId, purpose))
          .Then(_ => new Unit(), Errors.MapNone)
      );
  }

  // Account deletion: revoke whatever consents exist, skipping missing ones.
  public async Task<Result<Unit>> DisableAllPaymentConsentsAsync(string userId)
  {
    var customerRes = await repo.GetByUserId(userId);
    if (!customerRes.IsSuccess()) return customerRes.FailureOrDefault()!;
    var record = customerRes.Get()?.Principal.Record;
    if (record == null) return new Unit();

    foreach (var purpose in new[] { ConsentPurpose.Penalty, ConsentPurpose.Subscription })
    {
      if (string.IsNullOrEmpty(record.ConsentIdFor(purpose))) continue;
      var disabled = await gateway.DisablePaymentConsentAsync(record.ConsentIdFor(purpose)!)
        .ThenAwait(_ => repo.DisablePaymentConsentAsync(userId, purpose));
      if (!disabled.IsSuccess()) return disabled.FailureOrDefault()!;
    }

    return new Unit();
  }

  public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .ThenAwait(customer =>
        gateway.CreatePaymentIntentAsync(
          request with { CustomerId = customer.Principal.Record.AirwallexCustomerId }));
  }

  public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .ThenAwait(customer =>
        gateway.ConfirmPaymentIntentAsync(
          intentId,
          request.PaymentConsentId,
          customer.Principal.Record.AirwallexCustomerId));
  }

  // Merchant-initiated charge against the user's stored, verified consent.
  // No-consent surfaces a typed NotFoundException(PaymentCustomer) so the Penalty
  // drain can pattern-match it and mark the row Skipped.
  //
  // Exactly-once: when existingIntentId is supplied (a prior attempt that recorded
  // an intent) the gateway intent is retrieved and reconciled FIRST — if it already
  // SUCCEEDED it is returned without re-charging; otherwise that SAME intent is
  // confirmed rather than minting a new one. New charges derive request_id /
  // merchant_order_id from idempotencyKey so even a concurrent duplicate create
  // collapses onto one Airwallex intent.
  public Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
    string userId, Money amount, string description,
    string? idempotencyKey = null, string? existingIntentId = null,
    Func<string, Task>? onIntentCreated = null,
    ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .Then(customer =>
        !customer.Principal.Record.HasConsentFor(purpose) || string.IsNullOrEmpty(customer.Principal.Record.ConsentIdFor(purpose))
          ? new NotFoundException($"No verified {purpose} payment consent found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .ThenAwait(customer =>
      {
        var r = customer.Principal.Record;
        var consentId = r.ConsentIdFor(purpose)!;

        // Retry path: reconcile the previously-created intent instead of charging again.
        if (!string.IsNullOrEmpty(existingIntentId))
        {
          return gateway.RetrievePaymentIntentAsync(existingIntentId!)
            .ThenAwait(intent =>
              intent.Status == "SUCCEEDED"
                // Money already moved on the prior attempt: do not re-charge.
                ? intent.ToAsyncResult()
                // Not yet settled: confirm the SAME intent (no new intent id).
                : gateway.ConfirmPaymentIntentAsync(intent.Id, consentId, r.AirwallexCustomerId));
        }

        var createReq = new CreatePaymentIntentRequest
        {
          UserId = userId,
          CustomerId = r.AirwallexCustomerId,
          Amount = amount.Amount,
          Currency = amount.Currency.Code,
          Description = description,
          IdempotencyKey = idempotencyKey
        };
        return gateway.CreatePaymentIntentAsync(createReq)
          .ThenAwait(async intent =>
          {
            // Idempotent replay (same IdempotencyKey -> Airwallex request_id) returned the
            // already-settled intent from a prior attempt: do not re-confirm, which would
            // otherwise move money a second time.
            if (intent.Status == "SUCCEEDED") return intent.ToResult();

            // Persist the freshly-created intent id BEFORE confirming, so a confirm failure
            // (or crash) doesn't lose it. The next retry then reconciles THIS intent via
            // existingIntentId instead of creating a new one, which would reuse the
            // request_id and be rejected by Airwallex as duplicate_request.
            if (onIntentCreated != null) await onIntentCreated(intent.Id);

            return await gateway.ConfirmPaymentIntentAsync(intent.Id, consentId, r.AirwallexCustomerId);
          });
      });
  }

  public Task<Result<PaymentCustomer?>> GetCustomerById(Guid id)
  {
    throw new NotImplementedException();
  }

  public Task<Result<Unit>> CompletePaymentAsync(Guid requestId, PaymentRecord record)
  {
    throw new NotImplementedException();
  }

  public Task<Result<Unit>> UpdatePaymentStatusAsync(Guid requestId, PaymentRecord record)
  {
    throw new NotImplementedException();
  }
}
