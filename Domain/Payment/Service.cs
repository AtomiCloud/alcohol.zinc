using CSharp_Result;
using Domain.Exceptions;

namespace Domain.Payment;

public class PaymentService(
  IPaymentCustomerRepository repo,
  IPaymentIntentRepository intentRepo,
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

  // Update payment consent from webhook
  public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
    string airwallexCustomerId,
    string? paymentConsentId,
    PaymentConsentStatus? consentStatus)
  {
    return repo.UpdatePaymentConsentByAirwallexCustomerId(
      airwallexCustomerId,
      paymentConsentId,
      consentStatus);
  }

  public Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .Then(customer => new PaymentConsentStatusResult
      {
        ConsentId = customer.Principal.Record.PaymentConsentId,
        Status = customer.Principal.Record.ConsentStatus,
        HasPaymentConsent = customer.Principal.Record.HasPaymentConsent
      }, Errors.MapNone);
  }

  public Task<Result<bool>> HasPaymentConsentAsync(string userId)
  {
    return repo.GetByUserId(userId)
      .Then(customer => customer?.Principal.Record.HasPaymentConsent ?? false, Errors.MapNone);
  }

  public Task<Result<Unit>> DisablePaymentConsentAsync(string userId)
  {
    return repo.GetByUserId(userId)
      .Then(customer =>
        customer == null
          ? new NotFoundException($"Payment customer not found for userId: {userId}", typeof(PaymentCustomer), userId)
          : customer.ToResult()
      )
      .Then(customer =>
      {
        if (string.IsNullOrEmpty(customer.Principal.Record.PaymentConsentId))
        {
          return new NotFoundException($"No payment consent found for userId: {userId}", typeof(PaymentCustomer), userId);
        }
        return customer.ToResult();
      })
      .ThenAwait(customer =>
        gateway.DisablePaymentConsentAsync(customer.Principal.Record.PaymentConsentId!)
          .ThenAwait(_ => repo.DisablePaymentConsentAsync(userId))
          .Then(_ => new Unit(), Errors.MapNone)
      );
  }

  public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request)
  {
    // Generate ID before calling Airwallex so we can use it as merchant_order_id
    var paymentIntentId = Guid.NewGuid();
    var requestWithMerchantOrderId = request with { MerchantOrderId = paymentIntentId.ToString() };

    return gateway.CreatePaymentIntentAsync(requestWithMerchantOrderId)
      .ThenAwait(gatewayResult =>
      {
        // Save payment intent to database with the pre-generated ID
        var record = new PaymentIntentRecord
        {
          UserId = userId,
          AirwallexPaymentIntentId = gatewayResult.Id,
          AirwallexCustomerId = requestWithMerchantOrderId.CustomerId,
          Amount = requestWithMerchantOrderId.Amount,
          Currency = requestWithMerchantOrderId.Currency,
          CapturedAmount = 0m,
          Status = ParsePaymentIntentStatus(gatewayResult.Status),
          MerchantOrderId = gatewayResult.MerchantOrderId
        };

        return intentRepo.Create(paymentIntentId, record)
          .Then(_ => gatewayResult, Errors.MapNone);
      });
  }

  public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request)
  {
    return intentRepo.GetByAirwallexId(intentId)
      .Then(intent =>
        intent == null
          ? new NotFoundException($"Payment intent not found: {intentId}", typeof(PaymentIntent), intentId)
          : intent.ToResult()
      )
      .Then(intent =>
        intent.Principal.Record.UserId != userId
          ? new NotFoundException($"Payment intent does not belong to user: {userId}", typeof(PaymentIntent), userId)
          : intent.ToResult()
      )
      .ThenAwait(intent =>
        gateway.ConfirmPaymentIntentAsync(intentId, request.PaymentConsentId, intent.Principal.Record.AirwallexCustomerId)
          .ThenAwait(gatewayResult =>
          {
            // Update payment intent status in database
            var status = ParsePaymentIntentStatus(gatewayResult.Status);
            return intentRepo.UpdateStatus(intentId, status, 0m)
              .Then(_ => gatewayResult, Errors.MapNone);
          })
      );
  }

  public Task<Result<PaymentIntentPrincipal?>> UpdatePaymentIntentStatusAsync(
    string airwallexPaymentIntentId,
    PaymentIntentStatus status,
    decimal capturedAmount)
  {
    return intentRepo.UpdateStatus(airwallexPaymentIntentId, status, capturedAmount);
  }

  private static PaymentIntentStatus ParsePaymentIntentStatus(string status)
  {
    return status.ToUpperInvariant() switch
    {
      "REQUIRES_PAYMENT_METHOD" => PaymentIntentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentIntentStatus.RequiresCustomerAction,
      "REQUIRES_CAPTURE" => PaymentIntentStatus.RequiresCapture,
      "PENDING" => PaymentIntentStatus.Pending,
      "SUCCEEDED" => PaymentIntentStatus.Succeeded,
      "CANCELLED" => PaymentIntentStatus.Cancelled,
      _ => throw new ArgumentException($"Unknown payment intent status: {status}")
    }; 
  }

}
