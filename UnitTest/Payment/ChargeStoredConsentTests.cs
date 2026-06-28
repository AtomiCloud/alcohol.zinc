using CSharp_Result;
using Domain.Payment;
using FluentAssertions;
using NodaMoney;
using Xunit;

namespace UnitTest.Payment;

// Guards the duplicate_request fix: PaymentService.ChargeStoredConsentAsync must persist
// the freshly-created intent id (via onIntentCreated) AFTER create succeeds and BEFORE
// confirm, so a confirm failure doesn't lose it and retries reconcile the same intent.
public class ChargeStoredConsentTests
{
  private const string UserId = "user-1";
  private static readonly Money Amount = new(1.00m, Currency.FromCode("USD"));

  private static PaymentCustomer VerifiedCustomer() => new()
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
        PaymentConsentId = "cst_1",
        ConsentStatus = PaymentConsentStatus.Verified,
        HasPaymentConsent = true
      }
    }
  };

  private static PaymentIntentResult Intent(string id, string status) => new()
  {
    Id = id,
    Status = status,
    Amount = Amount.Amount,
    Currency = Amount.Currency.Code,
    CustomerId = "cus_1",
    MerchantOrderId = "mo_1"
  };

  [Fact]
  public async Task CreateOk_ConfirmFails_PersistsIntentId_BeforeConfirm()
  {
    var gateway = new FakeGateway
    {
      CreateResult = Intent("int_new", "REQUIRES_PAYMENT_METHOD"),
      ConfirmResult = new Exception("confirm 400 duplicate_request")
    };
    var svc = new PaymentService(new FakeCustomerRepo(VerifiedCustomer()), gateway);

    string? persisted = null;
    var res = await svc.ChargeStoredConsentAsync(
      UserId, Amount, "penalty",
      idempotencyKey: "pen-1",
      onIntentCreated: id => { persisted = id; gateway.MarkPersist(); return Task.CompletedTask; });

    // The created intent id was handed to the persistence hook...
    persisted.Should().Be("int_new");
    // ...before confirm ran (create -> persist -> confirm order)...
    gateway.Calls.Should().ContainInOrder("create", "persist", "confirm");
    // ...and the overall charge surfaces the confirm failure.
    res.IsSuccess().Should().BeFalse();
  }

  [Fact]
  public async Task RetryWithExistingIntent_ReconcilesNotRecreates()
  {
    var gateway = new FakeGateway
    {
      RetrieveResult = Intent("int_old", "REQUIRES_PAYMENT_METHOD"),
      ConfirmResult = Intent("int_old", "SUCCEEDED")
    };
    var svc = new PaymentService(new FakeCustomerRepo(VerifiedCustomer()), gateway);

    var hookCalled = false;
    var res = await svc.ChargeStoredConsentAsync(
      UserId, Amount, "penalty",
      idempotencyKey: "pen-1",
      existingIntentId: "int_old",
      onIntentCreated: _ => { hookCalled = true; return Task.CompletedTask; });

    res.IsSuccess().Should().BeTrue();
    gateway.Calls.Should().NotContain("create"); // reused existing intent
    hookCalled.Should().BeFalse();                // nothing new created -> hook not used
  }

  // --- Fakes ---

  private sealed class FakeCustomerRepo(PaymentCustomer customer) : IPaymentCustomerRepository
  {
    public Task<Result<PaymentCustomer?>> GetByUserId(string userId)
      => Task.FromResult<Result<PaymentCustomer?>>(customer);

    public Task<Result<PaymentCustomer?>> GetById(Guid id) => throw new NotImplementedException();
    public Task<Result<IEnumerable<PaymentCustomerPrincipal>>> Search(PaymentCustomerSearch search) => throw new NotImplementedException();
    public Task<Result<PaymentCustomerPrincipal>> Create(string userId, string airwallexCustomerId) => throw new NotImplementedException();
    public Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentByAirwallexCustomerId(
      string airwallexCustomerId, string? paymentConsentId, PaymentConsentStatus? consentStatus) => throw new NotImplementedException();
    public Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId) => throw new NotImplementedException();
  }

  private sealed class FakeGateway : IPaymentGateway
  {
    public List<string> Calls { get; } = [];
    public Result<PaymentIntentResult>? CreateResult { get; init; }
    public Result<PaymentIntentResult>? RetrieveResult { get; init; }
    public Result<PaymentIntentResult>? ConfirmResult { get; init; }

    public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
    {
      Calls.Add("create");
      return Task.FromResult(CreateResult!.Value);
    }

    public Task<Result<PaymentIntentResult>> RetrievePaymentIntentAsync(string paymentIntentId)
    {
      Calls.Add("retrieve");
      return Task.FromResult(RetrieveResult!.Value);
    }

    public Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string paymentIntentId, string paymentConsentId, string customerId)
    {
      Calls.Add("confirm");
      return Task.FromResult(ConfirmResult!.Value);
    }

    public void MarkPersist() => Calls.Add("persist");

    public Task<Result<string?>> GetCustomerIdByMerchantIdAsync(string merchantCustomerId) => throw new NotImplementedException();
    public Task<Result<string>> CreateCustomerAsync(string merchantCustomerId) => throw new NotImplementedException();
    public Task<Result<string>> GenerateClientSecretAsync(string customerId) => throw new NotImplementedException();
    public Task<Result<PaymentConsentInfo[]>> GetVerifiedPaymentConsentsAsync(string customerId) => throw new NotImplementedException();
    public Task<Result<Unit>> DisablePaymentConsentAsync(string paymentConsentId) => throw new NotImplementedException();
  }
}
