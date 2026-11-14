using App.Modules.Subscription.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Payment;
using Domain.Protection;
using Domain.Subscription;
using IntTest.Notification;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace IntTest.Subscription;

// During a rolling deploy the old and the new pod both run SubscriptionRenewalHostedService.
// Two renewal passes over the same due subscription must charge it once and roll it once.
// Real Postgres (PENALTY_TEST_DB), one DbContext per pass, as on two pods.
[Collection(DbIntegrationCollection.Name)]
public class RenewalOverlapTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping renewal overlap test.";
  private static readonly DateTime Now = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");
  private MainDbContext _db = null!;

  public async Task InitializeAsync()
  {
    if (_conn == null) return;
    _db = PenaltyIntegrationTests.NewContext(_conn);
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (_conn == null) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  private static SubscriptionManagementService Pass(MainDbContext db, IPaymentService payment) => new(
    new SubscriptionRepository(db, NullLogger<SubscriptionRepository>.Instance),
    new ProPlan(), payment, new NoopEmailNotifier(), new NoFreezeCap(),
    NullLogger<SubscriptionManagementService>.Instance);

  // The second pass runs while the first one is inside its charge: it has read the row as due
  // and holds the lease, but has not rolled the period yet. That is the overlap two pods have.
  [SkippableFact]
  public async Task Renewal_SecondPassDuringFirstCharge_ChargesOnceAndRollsOnce()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = $"user-{Guid.NewGuid():N}";
    var periodEnd = Now.AddDays(-1);
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.UserSubscriptions.Add(new UserSubscriptionData
    {
      UserId = userId, Tier = "pro", Status = (int)SubscriptionStatus.Active,
      PeriodStart = periodEnd.AddMonths(-1), PeriodEnd = periodEnd, CancelAtPeriodEnd = false
    });
    await _db.SaveChangesAsync();

    await using var podA = PenaltyIntegrationTests.NewContext(_conn!);
    await using var podB = PenaltyIntegrationTests.NewContext(_conn!);
    var payment = new GatewayStub();
    Result<int>? second = null;
    payment.DuringFirstCharge = async () => second = await Pass(podB, payment).ProcessRenewals(Now, 10);

    var first = await Pass(podA, payment).ProcessRenewals(Now, 10);

    first.Get().Should().Be(1);
    second!.Value.Get().Should().Be(0, "the first pass holds the renewal lease");
    payment.Charges.Should().Be(1);
    var row = await _db.UserSubscriptions.AsNoTracking().SingleAsync(x => x.UserId == userId);
    row.PeriodEnd.Should().Be(periodEnd.AddMonths(1), "rolled exactly once");
    row.Status.Should().Be((int)SubscriptionStatus.Active);
  }

  private sealed class ProPlan : ISubscriptionPlanProvider
  {
    public Result<SubscriptionPlan> GetPlan(string tier) => new SubscriptionPlan
    {
      Tier = tier, Price = new Money(4.99m, Currency.FromCode("USD")),
      Caps = new Dictionary<string, int>(), GracePeriodDays = 7
    };
  }

  private sealed class NoFreezeCap : IFreezeCapService
  {
    public Task<Result<FreezeBalance>> Reconcile(string userId)
      => Task.FromResult<Result<FreezeBalance>>(new FreezeBalance(0, 0));
  }

  // Counts charges; runs DuringFirstCharge inside the first one, after the intent is persisted.
  private sealed class GatewayStub : IPaymentService
  {
    public int Charges { get; private set; }
    public Func<Task>? DuringFirstCharge { get; set; }

    public async Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
      string userId, Money amount, string description,
      string? idempotencyKey = null, string? existingIntentId = null,
      Func<string, Task>? onIntentCreated = null,
      ConsentPurpose purpose = ConsentPurpose.Penalty)
    {
      Charges++;
      var intentId = $"int_{Charges}";
      if (existingIntentId == null && onIntentCreated != null) await onIntentCreated(intentId);
      var during = DuringFirstCharge;
      DuringFirstCharge = null;
      if (during != null) await during();
      return new PaymentIntentResult
      {
        Id = intentId, Status = "SUCCEEDED", Amount = amount.Amount, Currency = amount.Currency.Code,
        CustomerId = "cus_stub", MerchantOrderId = "mo_stub"
      };
    }

    public Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId) => throw new NotImplementedException();
    public Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId) => throw new NotImplementedException();
    public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
      string airwallexCustomerId, string? paymentConsentId, PaymentConsentStatus? consentStatus, ConsentPurpose purpose)
      => throw new NotImplementedException();
    public Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
      => throw new NotImplementedException();
    public Task<Result<bool>> HasPaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
      => throw new NotImplementedException();
    public Task<Result<Unit>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty)
      => throw new NotImplementedException();
    public Task<Result<Unit>> DisableAllPaymentConsentsAsync(string userId) => throw new NotImplementedException();
    public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request)
      => throw new NotImplementedException();
    public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request)
      => throw new NotImplementedException();
    public Task<Result<IEnumerable<PaymentCustomerPrincipal>>> SearchCustomers(PaymentCustomerSearch search)
      => throw new NotImplementedException();
    public Task<Result<PaymentCustomer?>> GetCustomerByUserId(string userId) => throw new NotImplementedException();
    public Task<Result<PaymentCustomer?>> GetCustomerById(Guid id) => throw new NotImplementedException();
    public Task<Result<Unit>> CompletePaymentAsync(Guid requestId, PaymentRecord record) => throw new NotImplementedException();
    public Task<Result<Unit>> UpdatePaymentStatusAsync(Guid requestId, PaymentRecord record) => throw new NotImplementedException();
  }
}
