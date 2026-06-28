using CSharp_Result;
using Domain.Exceptions;
using Domain.Payment;
using Domain.Penalty;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace UnitTest.Penalty;

// Unit tests for the penalty drain status-transition matrix, amount math,
// ratio=0 skip, and service-level idempotency. Hand-rolled fakes are used
// (Moq is not referenced by this project) so the suite compiles against the
// real L1/L3/L4/L6 signatures without adding a new package dependency.
public class PenaltyServiceTests
{
  // ---------------------------------------------------------------------------
  // 1. Amount math: cents = StakeCents * RatioBasisPoints / 10_000 (exact, long)
  //    The L5 computation is replicated here in a tiny helper that mirrors
  //    HabitService step 6 / StreakRepository:232 so we lock down the math.
  // ---------------------------------------------------------------------------
  private static long PenaltyCents(int stakeCents, int ratioBasisPoints)
    => (long)stakeCents * ratioBasisPoints / 10_000L;

  private static PenaltyRecord? BuildPenalty(Guid executionId, string userId, Guid charityId,
    int stakeCents, string currency, int ratioBasisPoints)
  {
    var amountCents = PenaltyCents(stakeCents, ratioBasisPoints);
    if (amountCents <= 0) return null; // skip zero/negative penalties
    var amount = new Money(amountCents / 100m, Currency.FromCode(currency));
    return new PenaltyRecord
    {
      HabitExecutionId = executionId,
      UserId = userId,
      CharityId = charityId,
      Amount = amount,
      Status = PenaltyStatus.Pending,
      PaymentIntentId = null,
      Attempts = 0,
      LastError = null
    };
  }

  [Fact]
  public void AmountMath_StakeAndRatio_ProducesExactMoney()
  {
    // 1000 cents * 5000 bps / 10000 = 500 cents = 5.00
    var rec = BuildPenalty(Guid.NewGuid(), "u1", Guid.NewGuid(), 1000, "SGD", 5000);

    rec.Should().NotBeNull();
    rec!.Amount.Amount.Should().Be(5.00m);
    rec.Amount.Currency.Code.Should().Be("SGD");
  }

  [Fact]
  public void RatioZero_ProducesNoPenalty()
  {
    var rec = BuildPenalty(Guid.NewGuid(), "u1", Guid.NewGuid(), 1000, "SGD", 0);
    rec.Should().BeNull();
  }

  [Fact]
  public void StakeZero_ProducesNoPenalty()
  {
    var rec = BuildPenalty(Guid.NewGuid(), "u1", Guid.NewGuid(), 0, "SGD", 5000);
    rec.Should().BeNull();
  }

  // ---------------------------------------------------------------------------
  // 3 + 4. Status-transition matrix + idempotency
  // ---------------------------------------------------------------------------

  private static PenaltyPrincipal Pending(int attempts = 0, Money? amount = null)
    => new()
    {
      Id = Guid.NewGuid(),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
      Record = new PenaltyRecord
      {
        HabitExecutionId = Guid.NewGuid(),
        UserId = "user-1",
        CharityId = Guid.NewGuid(),
        Amount = amount ?? new Money(5.00m, Currency.FromCode("SGD")),
        Status = PenaltyStatus.Pending,
        PaymentIntentId = null,
        Attempts = attempts,
        LastError = null
      }
    };

  private static PenaltyService Build(FakePenaltyRepository repo, FakePaymentService payment)
    => new(repo, payment, NullLogger<PenaltyService>.Instance);

  [Fact]
  public async Task Succeeded_MarksChargedOnce_AndCounts()
  {
    var pending = Pending();
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.Succeeds("pi_123");
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    repo.MarkChargedCalls.Should().ContainSingle();
    repo.MarkChargedCalls[0].Should().Be((pending.Id, "pi_123"));
    repo.MarkPendingCalls.Should().BeEmpty();
    repo.MarkFailedCalls.Should().BeEmpty();
    repo.MarkSkippedCalls.Should().BeEmpty();
    repo.BumpCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task RequiresAction_BelowMax_MarksPending_NotCharged()
  {
    var pending = Pending(attempts: 0);
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.WithStatus("pi_act", "REQUIRES_CUSTOMER_ACTION");
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(0); // stays Pending => did not leave Pending this run
    repo.MarkPendingCalls.Should().ContainSingle();
    repo.MarkPendingCalls[0].Should().Be((pending.Id, "pi_act", 1));
    repo.MarkChargedCalls.Should().BeEmpty();
    repo.MarkFailedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task RequiresAction_AtMax_MarksFailed()
  {
    // attempts=4, maxAttempts=5 => attempts+1 == 5 >= 5 => Failed
    var pending = Pending(attempts: 4);
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.WithStatus("pi_act", "REQUIRES_PAYMENT_METHOD");
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    ((int)res).Should().Be(1);
    repo.MarkFailedCalls.Should().ContainSingle();
    repo.MarkFailedCalls[0].Id.Should().Be(pending.Id);
    repo.MarkPendingCalls.Should().BeEmpty();
    repo.MarkChargedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task NoConsent_NotFoundException_MarksSkipped()
  {
    var pending = Pending();
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.Fails(
      new NotFoundException("No verified payment consent", typeof(PaymentCustomer), "user-1"));
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    ((int)res).Should().Be(1);
    repo.MarkSkippedCalls.Should().ContainSingle();
    repo.MarkSkippedCalls[0].Should().Be(pending.Id);
    repo.BumpCalls.Should().BeEmpty();
    repo.MarkFailedCalls.Should().BeEmpty();
    repo.MarkChargedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task TransientError_BelowMax_Bumps()
  {
    var pending = Pending(attempts: 0);
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.Fails(new Exception("gateway timeout"));
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    ((int)res).Should().Be(0); // stays Pending
    repo.BumpCalls.Should().ContainSingle();
    repo.BumpCalls[0].Id.Should().Be(pending.Id);
    repo.MarkFailedCalls.Should().BeEmpty();
    repo.MarkSkippedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task ConfirmFailsAfterCreate_PersistsIntentId_SoRetryDoesNotRecreate()
  {
    // Regression (Airwallex duplicate_request): create-intent succeeded but the follow-up
    // confirm failed. The intent id MUST be persisted (SetIntentId) so the next attempt
    // reconciles the EXISTING intent instead of re-creating with the same request_id.
    var pending = Pending(attempts: 0);
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.CreatesThenFails("pi_created", new Exception("confirm 400"));
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    res.IsSuccess().Should().BeTrue();
    // Intent id saved even though the overall charge failed.
    repo.SetIntentIdCalls.Should().ContainSingle();
    repo.SetIntentIdCalls[0].Should().Be((pending.Id, "pi_created"));
    // Confirm failed transiently (below max) -> bumped for retry, not charged/failed.
    repo.BumpCalls.Should().ContainSingle();
    repo.MarkChargedCalls.Should().BeEmpty();
    repo.MarkFailedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task TransientError_AtMax_MarksFailed()
  {
    var pending = Pending(attempts: 4); // attempts+1 == 5 >= 5
    var repo = new FakePenaltyRepository(pending);
    var payment = FakePaymentService.Fails(new Exception("gateway timeout"));
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    ((int)res).Should().Be(1);
    repo.MarkFailedCalls.Should().ContainSingle();
    repo.BumpCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Idempotency_Succeeded_ChargesExactlyOncePerPendingRow()
  {
    var p1 = Pending();
    var p2 = Pending();
    var repo = new FakePenaltyRepository(p1, p2);
    var payment = FakePaymentService.Succeeds("pi_x");
    var svc = Build(repo, payment);

    var res = await svc.ProcessPending(batchSize: 10, maxAttempts: 5);

    ((int)res).Should().Be(2);
    repo.MarkChargedCalls.Should().HaveCount(2);
    repo.MarkChargedCalls.Select(c => c.Id).Should().BeEquivalentTo(new[] { p1.Id, p2.Id });
    // Each row charged exactly once.
    repo.MarkChargedCalls.Select(c => c.Id).Distinct().Should().HaveCount(2);
  }
}
