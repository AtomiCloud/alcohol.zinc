using CSharp_Result;
using Domain.Payment;
using Domain.Subscription;
using NodaMoney;

namespace UnitTest.Subscription;

// Hand-rolled fakes implementing the real Domain.Subscription contracts (no Moq,
// matching the UnitTest/Penalty style) so the management/renewal tests can
// assert the full state-transition matrix.

public sealed class FakeSubscriptionRepository : ISubscriptionRepository
{
  private readonly Dictionary<string, UserSubscriptionPrincipal> _byUser = [];

  public List<string> Calls { get; } = [];
  public List<(Guid Id, string IntentId, string ChargeKey)> SetIntentIdCalls { get; } = [];

  public FakeSubscriptionRepository(params UserSubscriptionPrincipal[] rows)
  {
    foreach (var r in rows) _byUser[r.Record.UserId] = r;
  }

  public UserSubscriptionPrincipal? Row(string userId) => _byUser.GetValueOrDefault(userId);

  private UserSubscriptionPrincipal? ById(Guid id) => _byUser.Values.FirstOrDefault(x => x.Id == id);

  private void Put(UserSubscriptionPrincipal p) => _byUser[p.Record.UserId] = p;

  private Result<Unit> Mutate(Guid id, Func<UserSubscriptionRecord, UserSubscriptionRecord> f)
  {
    var row = this.ById(id);
    if (row == null) return new InvalidOperationException($"UserSubscription {id} not found");
    this.Put(row with { Record = f(row.Record), UpdatedAt = DateTime.UtcNow });
    return new Unit();
  }

  public Task<Result<UserSubscriptionPrincipal?>> GetByUserId(string userId)
  {
    Calls.Add($"GetByUserId({userId})");
    return Task.FromResult<Result<UserSubscriptionPrincipal?>>(this.Row(userId));
  }

  public Task<Result<UserSubscriptionPrincipal>> Upsert(UserSubscriptionRecord record)
  {
    Calls.Add($"Upsert({record.UserId},{record.Tier},{record.Status})");
    var existing = this.Row(record.UserId);
    var p = new UserSubscriptionPrincipal
    {
      Id = existing?.Id ?? Guid.NewGuid(),
      Record = record,
      CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    this.Put(p);
    return Task.FromResult<Result<UserSubscriptionPrincipal>>(p);
  }

  public Task<Result<Unit>> SetIntentId(Guid id, string intentId, string chargeKey)
  {
    Calls.Add($"SetIntentId({intentId},{chargeKey})");
    SetIntentIdCalls.Add((id, intentId, chargeKey));
    return Task.FromResult(this.Mutate(id, r => r with
    {
      LastChargeIntentId = intentId,
      LastChargeKey = chargeKey
    }));
  }

  public Task<Result<bool>> Activate(Guid id, string tier, DateTime periodStart, DateTime periodEnd, DateTime nowUtc)
  {
    Calls.Add($"Activate({tier})");
    var row = this.ById(id);
    if (row == null)
      return Task.FromResult<Result<bool>>(new InvalidOperationException($"UserSubscription {id} not found"));
    // Mirror the real repository's lease guard.
    if (row.Record.RenewingUntil is { } lease && lease >= nowUtc)
      return Task.FromResult<Result<bool>>(false);
    this.Put(row with
    {
      Record = row.Record with
      {
        Tier = tier,
        Status = SubscriptionStatus.Active,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        CancelAtPeriodEnd = false,
        NextTier = null,
        LastChargeIntentId = null,
        LastChargeKey = null
      },
      UpdatedAt = DateTime.UtcNow
    });
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<bool>> RollPeriod(Guid id, string tier, DateTime periodStart, DateTime periodEnd)
  {
    Calls.Add($"RollPeriod({tier})");
    var row = _byUser.Values.FirstOrDefault(x => x.Id == id);
    if (row == null)
      return Task.FromResult<Result<bool>>(new InvalidOperationException($"UserSubscription {id} not found"));
    // Mirror the real repository's stale-roll guard.
    if (row.Record.PeriodEnd != periodStart) return Task.FromResult<Result<bool>>(false);
    this.Put(row with
    {
      Record = row.Record with
      {
        Tier = tier,
        Status = SubscriptionStatus.Active,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        // Preserve a NextTier scheduled mid-renewal (real repo semantics).
        NextTier = row.Record.NextTier == tier ? null : row.Record.NextTier,
        LastChargeIntentId = null,
        LastChargeKey = null
      },
      UpdatedAt = DateTime.UtcNow
    });
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<Unit>> SetCancelAtPeriodEnd(Guid id, bool value)
  {
    Calls.Add($"SetCancelAtPeriodEnd({value})");
    return Task.FromResult(this.Mutate(id, r => r with { CancelAtPeriodEnd = value }));
  }

  public Task<Result<Unit>> SetNextTier(Guid id, string? nextTier)
  {
    Calls.Add($"SetNextTier({nextTier ?? "null"})");
    return Task.FromResult(this.Mutate(id, r => r with { NextTier = nextTier }));
  }

  public Task<Result<Unit>> MarkGrace(Guid id)
  {
    Calls.Add("MarkGrace");
    return Task.FromResult(this.Mutate(id, r => r with { Status = SubscriptionStatus.Grace }));
  }

  public Task<Result<Unit>> MarkCancelled(Guid id)
  {
    Calls.Add("MarkCancelled");
    return Task.FromResult(this.Mutate(id, r => r with
    {
      Status = SubscriptionStatus.Cancelled,
      CancelAtPeriodEnd = false,
      NextTier = null,
      LastChargeIntentId = null,
      LastChargeKey = null
    }));
  }

  public Task<Result<bool>> TryClaimRenewal(Guid id, DateTime expectedPeriodEnd, DateTime until, DateTime nowUtc)
  {
    Calls.Add("TryClaimRenewal");
    var row = this.ById(id);
    if (row == null)
      return Task.FromResult<Result<bool>>(new InvalidOperationException($"UserSubscription {id} not found"));
    // Mirror the real repository's guards: expected period + no live lease.
    if (row.Record.PeriodEnd != expectedPeriodEnd
        || (row.Record.RenewingUntil is { } lease && lease >= nowUtc))
      return Task.FromResult<Result<bool>>(false);
    this.Put(row with { Record = row.Record with { RenewingUntil = until } });
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<Unit>> ReleaseRenewal(Guid id, DateTime until)
  {
    Calls.Add("ReleaseRenewal");
    var row = this.ById(id);
    if (row == null)
      return Task.FromResult<Result<Unit>>(new InvalidOperationException($"UserSubscription {id} not found"));
    // Only release our own lease (real repo semantics).
    if (row.Record.RenewingUntil == until)
      this.Put(row with { Record = row.Record with { RenewingUntil = null } });
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<List<UserSubscriptionPrincipal>>> GetDue(DateTime nowUtc, int batchSize)
  {
    Calls.Add("GetDue");
    var due = _byUser.Values
      .Where(x => x.Record.Status is SubscriptionStatus.Active or SubscriptionStatus.Grace
                  && x.Record.PeriodEnd <= nowUtc)
      .Take(batchSize)
      .ToList();
    return Task.FromResult<Result<List<UserSubscriptionPrincipal>>>(due);
  }


}

public sealed class FakePlanProvider(Dictionary<string, SubscriptionPlan> plans) : ISubscriptionPlanProvider
{
  public static FakePlanProvider Default()
  {
    return new FakePlanProvider(new Dictionary<string, SubscriptionPlan>
    {
      ["free"] = Plan("free", 0m),
      ["pro"] = Plan("pro", 4.99m),
      ["ultimate"] = Plan("ultimate", 7.99m)
    });
  }

  public static SubscriptionPlan Plan(string tier, decimal price, int graceDays = 7)
    => new()
    {
      Tier = tier,
      Price = new Money(price, Currency.FromCode("USD")),
      Caps = new Dictionary<string, int>
      {
        ["ent.habits.max"] = tier switch { "free" => 2, "pro" => 10, _ => int.MaxValue },
        ["ent.skips.monthly"] = tier switch { "free" => 10, "pro" => 20, _ => 60 },
        ["ent.vacation.windows.yearly"] = tier switch { "free" => 0, "pro" => 6, _ => 12 },
        ["ent.freeze.base"] = tier switch { "free" => 0, "pro" => 14, _ => 30 }
      },
      GracePeriodDays = graceDays
    };

  public Result<SubscriptionPlan> GetPlan(string tier)
    => plans.TryGetValue(tier, out var p) ? p : new InvalidSubscriptionTierException(tier);
}


// Payment fake tailored to the subscription flows: implements
// HasPaymentConsentAsync + ChargeStoredConsentAsync (the penalty fake throws on
// the former). Same static factory style as UnitTest/Penalty/Fakes.cs.
public sealed class FakeSubscriptionPaymentService : IPaymentService
{
  private readonly Func<string, Money, string, Result<PaymentIntentResult>> _charge;
  private readonly string? _emitIntentId;

  public bool HasConsent { get; init; } = true;

  private FakeSubscriptionPaymentService(
    Func<string, Money, string, Result<PaymentIntentResult>> charge, string? emitIntentId = null)
  {
    _charge = charge;
    _emitIntentId = emitIntentId;
  }

  public List<(string UserId, Money Amount, string Description, string? IdempotencyKey, string? ExistingIntentId, ConsentPurpose Purpose)>
    ChargeCalls { get; } = [];

  public List<ConsentPurpose> HasConsentCalls { get; } = [];

  public static FakeSubscriptionPaymentService Succeeds(string intentId)
    => WithStatus(intentId, "SUCCEEDED");

  public static FakeSubscriptionPaymentService WithStatus(string intentId, string status)
    => new((_, amount, _) => new PaymentIntentResult
    {
      Id = intentId,
      Status = status,
      Amount = amount.Amount,
      Currency = amount.Currency.Code,
      CustomerId = "cus_fake",
      MerchantOrderId = "mo_fake"
    });

  public static FakeSubscriptionPaymentService Fails(Exception ex)
    => new((_, _, _) => ex);

  // Simulates: intent created (emits intentId via onIntentCreated), then confirm failed.
  public static FakeSubscriptionPaymentService CreatesThenFails(string intentId, Exception ex)
    => new((_, _, _) => ex, emitIntentId: intentId);

  public static FakeSubscriptionPaymentService NoConsent()
    => new((_, _, _) => new Exception("should not be charged")) { HasConsent = false };

  public Task<Result<bool>> HasPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    HasConsentCalls.Add(purpose);
    return Task.FromResult<Result<bool>>(this.HasConsent);
  }

  public async Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
    string userId, Money amount, string description,
    string? idempotencyKey = null, string? existingIntentId = null,
    Func<string, Task>? onIntentCreated = null,
    ConsentPurpose purpose = ConsentPurpose.Penalty)
  {
    ChargeCalls.Add((userId, amount, description, idempotencyKey, existingIntentId, purpose));
    if (_emitIntentId != null && onIntentCreated != null)
      await onIntentCreated(_emitIntentId);
    return _charge(userId, amount, description);
  }

  // --- Unused members of the contract (subscription flows never invoke these) ---
  public Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId)
    => throw new NotImplementedException();

  public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
    string airwallexCustomerId, string? paymentConsentId, PaymentConsentStatus? consentStatus, ConsentPurpose purpose)
    => throw new NotImplementedException();

  public Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
    => throw new NotImplementedException();

  public Task<Result<Unit>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
    => throw new NotImplementedException();

  public Task<Result<Unit>> DisableAllPaymentConsentsAsync(string userId)
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
