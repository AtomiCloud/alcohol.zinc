using CSharp_Result;
using Domain.Payment;
using Domain.Penalty;
using NodaMoney;

namespace UnitTest.Penalty;

// Minimal hand-rolled fakes implementing the real L3/L4 contracts so the
// service unit tests can assert on the status-transition matrix without
// pulling in a mocking framework (Moq is not referenced by UnitTest).

public sealed class FakePenaltyRepository(params PenaltyPrincipal[] pending) : IPenaltyRepository
{
  private readonly List<PenaltyPrincipal> _pending = [.. pending];

  public List<PenaltyRecord> EnqueuedRecords { get; } = [];
  public List<(Guid Id, string PaymentIntentId)> MarkChargedCalls { get; } = [];
  public List<(Guid Id, string PaymentIntentId, int Attempts)> MarkPendingCalls { get; } = [];
  public List<(Guid Id, string PaymentIntentId)> SetIntentIdCalls { get; } = [];
  public List<(Guid Id, string Error)> BumpCalls { get; } = [];
  public List<(Guid Id, string Error)> MarkFailedCalls { get; } = [];
  public List<Guid> MarkSkippedCalls { get; } = [];

  public Task<Result<bool>> EnqueuePending(PenaltyRecord record)
  {
    // Idempotent: no-op if HabitExecutionId already enqueued.
    var exists = EnqueuedRecords.Any(r => r.HabitExecutionId == record.HabitExecutionId);
    if (exists) return Task.FromResult<Result<bool>>(false);
    EnqueuedRecords.Add(record);
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<List<PenaltyPrincipal>>> GetPending(int batchSize)
    => Task.FromResult<Result<List<PenaltyPrincipal>>>(_pending.Take(batchSize).ToList());

  public Task<Result<Unit>> MarkCharged(Guid id, string paymentIntentId)
  {
    MarkChargedCalls.Add((id, paymentIntentId));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> MarkPending(Guid id, string paymentIntentId, int attempts)
  {
    MarkPendingCalls.Add((id, paymentIntentId, attempts));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> SetIntentId(Guid id, string paymentIntentId)
  {
    SetIntentIdCalls.Add((id, paymentIntentId));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> Bump(Guid id, string error)
  {
    BumpCalls.Add((id, error));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> MarkFailed(Guid id, string error)
  {
    MarkFailedCalls.Add((id, error));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> MarkSkipped(Guid id)
  {
    MarkSkippedCalls.Add(id);
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<List<PenaltyPrincipal>>> Search(PenaltySearch search)
    => Task.FromResult<Result<List<PenaltyPrincipal>>>(new List<PenaltyPrincipal>());
}

public sealed class FakePaymentService : IPaymentService
{
  private readonly Func<string, Money, string, Result<PaymentIntentResult>> _charge;
  // If set, ChargeStoredConsentAsync invokes onIntentCreated with this id before returning,
  // simulating "create succeeded, then confirm did X".
  private readonly string? _emitIntentId;

  private FakePaymentService(Func<string, Money, string, Result<PaymentIntentResult>> charge, string? emitIntentId = null)
  {
    _charge = charge;
    _emitIntentId = emitIntentId;
  }

  public List<(string UserId, Money Amount, string Description)> ChargeCalls { get; } = [];

  public static FakePaymentService Succeeds(string intentId)
    => WithStatus(intentId, "SUCCEEDED");

  public static FakePaymentService WithStatus(string intentId, string status)
    => new((userId, amount, _) => new PaymentIntentResult
    {
      Id = intentId,
      Status = status,
      Amount = amount.Amount,
      Currency = amount.Currency.Code,
      CustomerId = "cus_fake",
      MerchantOrderId = "mo_fake"
    });

  public static FakePaymentService Fails(Exception ex)
    => new((_, _, _) => ex);

  // Simulates: create intent succeeded (emits intentId via onIntentCreated), then the
  // subsequent confirm failed with ex. Mirrors the real bug scenario.
  public static FakePaymentService CreatesThenFails(string intentId, Exception ex)
    => new((_, _, _) => ex, emitIntentId: intentId);

  public async Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
    string userId, Money amount, string description,
    string? idempotencyKey = null, string? existingIntentId = null,
    Func<string, Task>? onIntentCreated = null)
  {
    ChargeCalls.Add((userId, amount, description));
    if (_emitIntentId != null && onIntentCreated != null)
      await onIntentCreated(_emitIntentId);
    return _charge(userId, amount, description);
  }

  // --- Unused members of the contract (drain never invokes these) ---
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
