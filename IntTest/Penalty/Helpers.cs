using CSharp_Result;
using Domain.Payment;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace IntTest.Penalty;

// Minimal IOptionsMonitor returning a fixed value, so MainDbContext can be
// constructed directly in a test without the full DI/config stack.
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
  public T CurrentValue { get; } = value;
  public T Get(string? name) => CurrentValue;
  public IDisposable? OnChange(Action<T, string?> listener) => null;
}

// Stub payment service: only ChargeStoredConsentAsync is exercised by the drain.
// Everything else throws (never reached by PenaltyService.ProcessPending).
public sealed class StubPaymentService(Func<string, Money, string, Result<PaymentIntentResult>> charge)
  : IPaymentService
{
  public static StubPaymentService Succeeds(string intentId)
    => new((_, amount, _) => new PaymentIntentResult
    {
      Id = intentId,
      Status = "SUCCEEDED",
      Amount = amount.Amount,
      Currency = amount.Currency.Code,
      CustomerId = "cus_stub",
      MerchantOrderId = "mo_stub"
    });

  public static StubPaymentService Fails(Exception ex)
    => new((_, _, _) => ex);

  // Round-trip stub: 1st charge (existingIntentId == null) "creates" an intent (emits the id
  // via onIntentCreated, mirroring a successful create) then fails confirm; the 2nd charge
  // (now called with existingIntentId set) reconciles to SUCCEEDED.
  public static StubPaymentService CreatesThenFailsThenSucceeds(string intentId)
  {
    var calls = 0;
    return new StubPaymentService((_, amount, _) =>
    {
      calls++;
      return calls == 1
        ? (Result<PaymentIntentResult>)new Exception("confirm 400 duplicate_request")
        : new PaymentIntentResult
        {
          Id = intentId,
          Status = "SUCCEEDED",
          Amount = amount.Amount,
          Currency = amount.Currency.Code,
          CustomerId = "cus_stub",
          MerchantOrderId = "mo_stub"
        };
    })
    { EmitIntentIdOnFirstCreate = intentId };
  }

  // Set by CreatesThenFailsThenSucceeds: emit this id via onIntentCreated on the first
  // (create) call so the caller persists it before the simulated confirm failure.
  public string? EmitIntentIdOnFirstCreate { get; init; }

  // Records the existingIntentId passed on each call, so a test can assert the retry
  // reconciled the persisted intent (non-null) instead of re-creating (null).
  public List<string?> ExistingIntentIdArgs { get; } = [];

  public async Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
    string userId, Money amount, string description,
    string? idempotencyKey = null, string? existingIntentId = null,
    Func<string, Task>? onIntentCreated = null)
  {
    ExistingIntentIdArgs.Add(existingIntentId);
    if (EmitIntentIdOnFirstCreate != null && existingIntentId == null && onIntentCreated != null)
      await onIntentCreated(EmitIntentIdOnFirstCreate);
    return charge(userId, amount, description);
  }

  public Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
    string airwallexCustomerId, string? paymentConsentId, PaymentConsentStatus? consentStatus)
    => throw new NotImplementedException();

  public Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<bool>> HasPaymentConsentAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<Unit>> DisablePaymentConsentAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request)
    => throw new NotImplementedException();

  public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request)
    => throw new NotImplementedException();

  public Task<Result<IEnumerable<PaymentCustomerPrincipal>>> SearchCustomers(PaymentCustomerSearch search)
    => throw new NotImplementedException();

  public Task<Result<PaymentCustomer?>> GetCustomerByUserId(string userId)
    => throw new NotImplementedException();

  public Task<Result<PaymentCustomer?>> GetCustomerById(Guid id)
    => throw new NotImplementedException();

  public Task<Result<Unit>> CompletePaymentAsync(Guid requestId, PaymentRecord record)
    => throw new NotImplementedException();

  public Task<Result<Unit>> UpdatePaymentStatusAsync(Guid requestId, PaymentRecord record)
    => throw new NotImplementedException();
}
