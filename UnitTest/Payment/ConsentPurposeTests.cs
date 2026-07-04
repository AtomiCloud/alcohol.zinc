using CSharp_Result;
using Domain.Exceptions;
using Domain.Payment;
using NodaMoney;

namespace UnitTest.Payment;

// Purpose-scoped consents: penalty charges ride the unscheduled-MIT consent,
// subscription charges the recurring-MIT one. A charge must only ever confirm
// against ITS purpose's consent, and must refuse when that consent is absent —
// even if the other purpose's consent exists.
public class ConsentPurposeTests
{
  private const string UserId = "u1";

  private static PaymentCustomer Customer(string? penaltyConsent, string? subscriptionConsent) => new()
  {
    Principal = new PaymentCustomerPrincipal
    {
      Id = Guid.NewGuid(),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
      Record = new PaymentCustomerRecord
      {
        UserId = UserId,
        AirwallexCustomerId = "cus_1",
        Consents = Consents(penaltyConsent, subscriptionConsent)
      }
    }
  };

  private static Dictionary<ConsentPurpose, StoredPaymentConsent> Consents(
    string? penaltyConsent, string? subscriptionConsent)
  {
    var d = new Dictionary<ConsentPurpose, StoredPaymentConsent>();
    if (penaltyConsent != null)
      d[ConsentPurpose.Penalty] = new StoredPaymentConsent { ConsentId = penaltyConsent, Status = PaymentConsentStatus.Verified };
    if (subscriptionConsent != null)
      d[ConsentPurpose.Subscription] = new StoredPaymentConsent { ConsentId = subscriptionConsent, Status = PaymentConsentStatus.Verified };
    return d;
  }

  private static PaymentIntentResult Intent(string id, string status) => new()
  {
    Id = id,
    Status = status,
    Amount = 5m,
    Currency = "USD",
    CustomerId = "cus_1",
    MerchantOrderId = "mo_1"
  };

  // Gateway that records which consent id each confirm used.
  private sealed class RecordingGateway : IPaymentGateway
  {
    public List<string> ConfirmedConsentIds { get; } = [];

    public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
      => Task.FromResult<Result<PaymentIntentResult>>(Intent("int_1", "REQUIRES_CUSTOMER_ACTION"));

    public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string paymentIntentId, string paymentConsentId, string customerId)
    {
      ConfirmedConsentIds.Add(paymentConsentId);
      return Task.FromResult<Result<PaymentIntentResult>>(Intent(paymentIntentId, "SUCCEEDED"));
    }

    public Task<Result<PaymentIntentResult>> RetrievePaymentIntentAsync(string paymentIntentId)
      => Task.FromResult<Result<PaymentIntentResult>>(Intent(paymentIntentId, "REQUIRES_CUSTOMER_ACTION"));

    public List<string> DisabledConsentIds { get; } = [];
    public string? FailDisableFor { get; set; }

    public Task<Result<Unit>> DisablePaymentConsentAsync(string paymentConsentId)
    {
      if (paymentConsentId == FailDisableFor)
        return Task.FromResult<Result<Unit>>(new Exception($"gateway failed disabling {paymentConsentId}"));
      DisabledConsentIds.Add(paymentConsentId);
      return Task.FromResult<Result<Unit>>(new Unit());
    }

    public Task<Result<string?>> GetCustomerIdByMerchantIdAsync(string merchantCustomerId) => throw new NotImplementedException();
    public Task<Result<string>> CreateCustomerAsync(string merchantCustomerId) => throw new NotImplementedException();
    public Task<Result<string>> GenerateClientSecretAsync(string customerId) => throw new NotImplementedException();
    public Task<Result<PaymentConsentInfo[]>> GetVerifiedPaymentConsentsAsync(string customerId) => throw new NotImplementedException();
  }

  private sealed class FakeRepo(PaymentCustomer customer) : IPaymentCustomerRepository
  {
    public Task<Result<PaymentCustomer?>> GetByUserId(string userId)
      => Task.FromResult<Result<PaymentCustomer?>>(customer);

    public Task<Result<PaymentCustomer?>> GetById(Guid id) => throw new NotImplementedException();
    public Task<Result<IEnumerable<PaymentCustomerPrincipal>>> Search(PaymentCustomerSearch search) => throw new NotImplementedException();
    public Task<Result<PaymentCustomerPrincipal>> Create(string userId, string airwallexCustomerId) => throw new NotImplementedException();
    public List<ConsentPurpose> DisabledPurposes { get; } = [];

    public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentByAirwallexCustomerId(
      string airwallexCustomerId, string? paymentConsentId, PaymentConsentStatus? consentStatus, ConsentPurpose purpose) => throw new NotImplementedException();

    public Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose)
    {
      DisabledPurposes.Add(purpose);
      return Task.FromResult<Result<PaymentCustomerPrincipal?>>(customer.Principal);
    }
  }

  private static PaymentService Svc(PaymentCustomer customer, RecordingGateway gateway)
    => new(new FakeRepo(customer), gateway);

  [Fact]
  public async Task Charge_SubscriptionPurpose_ConfirmsWithSubscriptionConsent()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.ChargeStoredConsentAsync(
      UserId, new Money(5m, Currency.FromCode("USD")), "sub fee",
      purpose: ConsentPurpose.Subscription);

    res.IsSuccess().Should().BeTrue();
    gateway.ConfirmedConsentIds.Should().ContainSingle().Which.Should().Be("cst_sub");
  }

  [Fact]
  public async Task Charge_DefaultPurpose_ConfirmsWithPenaltyConsent()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.ChargeStoredConsentAsync(
      UserId, new Money(5m, Currency.FromCode("USD")), "penalty");

    res.IsSuccess().Should().BeTrue();
    gateway.ConfirmedConsentIds.Should().ContainSingle().Which.Should().Be("cst_pen");
  }

  [Fact]
  public async Task Charge_SubscriptionPurpose_WithOnlyPenaltyConsent_Refuses()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: null), gateway);

    var res = await svc.ChargeStoredConsentAsync(
      UserId, new Money(5m, Currency.FromCode("USD")), "sub fee",
      purpose: ConsentPurpose.Subscription);

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<NotFoundException>(
      "the penalty consent must never be able to pay for a subscription");
    gateway.ConfirmedConsentIds.Should().BeEmpty();
  }

  [Fact]
  public async Task Charge_PenaltyPurpose_WithOnlySubscriptionConsent_Refuses()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: null, subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.ChargeStoredConsentAsync(
      UserId, new Money(5m, Currency.FromCode("USD")), "penalty");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<NotFoundException>();
    gateway.ConfirmedConsentIds.Should().BeEmpty();
  }

  [Fact]
  public async Task DisableAll_RevokesBothPurposes()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.DisableAllPaymentConsentsAsync(UserId);

    res.IsSuccess().Should().BeTrue();
    gateway.DisabledConsentIds.Should().BeEquivalentTo("cst_pen", "cst_sub");
  }

  [Fact]
  public async Task DisableAll_SkipsMissingConsents()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: null, subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.DisableAllPaymentConsentsAsync(UserId);

    res.IsSuccess().Should().BeTrue();
    gateway.DisabledConsentIds.Should().BeEquivalentTo("cst_sub");
  }

  [Fact]
  public async Task DisableAll_PenaltyFailure_StillRevokesSubscription()
  {
    // Aborting on the first failure would orphan the other mandate at Airwallex
    // forever (the stored id is purged right after during account deletion).
    var gateway = new RecordingGateway { FailDisableFor = "cst_pen" };
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.DisableAllPaymentConsentsAsync(UserId);

    res.IsSuccess().Should().BeFalse("the failure must still be surfaced after all attempts");
    gateway.DisabledConsentIds.Should().BeEquivalentTo("cst_sub");
  }

  [Fact]
  public async Task Charge_ReconcileBranch_ConfirmsWithSubscriptionConsent()
  {
    // The existingIntentId retry path must also use the purpose's consent.
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: "cst_sub"), gateway);

    var res = await svc.ChargeStoredConsentAsync(
      UserId, new Money(5m, Currency.FromCode("USD")), "sub renewal",
      existingIntentId: "int_prev",
      purpose: ConsentPurpose.Subscription);

    res.IsSuccess().Should().BeTrue();
    gateway.ConfirmedConsentIds.Should().ContainSingle().Which.Should().Be("cst_sub");
  }

  [Fact]
  public async Task HasPaymentConsent_IsPurposeScoped()
  {
    var gateway = new RecordingGateway();
    var svc = Svc(Customer(penaltyConsent: "cst_pen", subscriptionConsent: null), gateway);

    ((bool)await svc.HasPaymentConsentAsync(UserId)).Should().BeTrue();
    ((bool)await svc.HasPaymentConsentAsync(UserId, ConsentPurpose.Subscription)).Should().BeFalse();
  }
}
