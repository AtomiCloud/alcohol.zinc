using CSharp_Result;
using Domain.Habit;
using Domain.Penalty;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using UnitTest.Penalty;

namespace UnitTest.Protection;

// Unit tests for the mark-as-fail cron orchestration (HabitService.MarkDailyFailures):
//   - protected statuses (completed/skipped/frozen/vacationed) are NOT failed
//   - a true miss IS failed and enqueues exactly one Pending penalty
//   - idempotent re-run does not double-enqueue
//   - ratio=0 / stake=0 -> failed row recorded but NO penalty enqueued
//   - ordering: vacation insert -> freeze insert -> fail step
//
// Uses the shared hand-rolled fakes in UnitTest/Protection/Fakes.cs and reuses
// the existing UnitTest.Penalty.FakePenaltyRepository.
public class MarkDailyFailuresTests
{
  private const string UserId = "user-1";

  private sealed record Harness(
    FakeHabitRepository Repo,
    FakeVacationRepository Vac,
    FakeProtectionRepository Prot,
    FakeEntitlementService Ent,
    FakePenaltyRepository Penalty,
    HabitService Svc);

  // Build a service wired to fresh fakes. freezeBalance configures the freeze pool.
  private static Harness Build(int freezeBalance = 0)
  {
    var log = new CallLog();
    var repo = new FakeHabitRepository(log);
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: freezeBalance);
    var ent = new FakeEntitlementService();
    var penalty = new FakePenaltyRepository();
    var svc = new HabitService(repo, vac, prot, ent, penalty, NullLogger<HabitService>.Instance);
    return new Harness(repo, vac, prot, ent, penalty, svc);
  }

  // Seed one habit + its scheduled version for `date` and return (habitId, hvId).
  private static (Guid HabitId, Guid HvId) SeedScheduled(Harness h, string userId = UserId)
  {
    var habitId = Guid.NewGuid();
    var hvId = Guid.NewGuid();
    h.Repo.Habits.Add(HabitFakeFactory.Habit(habitId, userId));
    h.Repo.ActiveVersions.Add(HabitFakeFactory.Version(hvId, habitId));
    return (habitId, hvId);
  }

  // ---------------------------------------------------------------------------
  // HAPPY: a true miss IS failed and enqueues exactly one Pending penalty.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task TrueMiss_IsFailed_AndEnqueuesExactlyOnePendingPenalty()
  {
    var h = Build();
    var (habitId, hvId) = SeedScheduled(h);
    var execId = Guid.NewGuid();
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId, ExecutionId = execId };
    var date = new DateOnly(2026, 1, 1);

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1); // 1 failed row, no protected/frozen

    h.Repo.CreateFailedExecutionsCalls.Should().ContainSingle();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();

    h.Penalty.EnqueuedRecords.Should().ContainSingle();
    var rec = h.Penalty.EnqueuedRecords[0];
    rec.HabitExecutionId.Should().Be(execId);
    rec.UserId.Should().Be(UserId);
    rec.Status.Should().Be(PenaltyStatus.Pending);
    // 1000c * 5000bps / 10000 = 500c = $5.00 USD
    rec.Amount.Should().Be(new Money(5.00m, Currency.FromCode("USD")));

    // The freeze branch is probed for any not-done habit, but with an empty pool
    // (freezeBalance: 0) nothing is consumed and no Freeze execution is inserted.
    h.Prot.TryConsumeFreezeCalls.Should().ContainSingle();
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze)
      .Should().BeEmpty();
  }

  // ---------------------------------------------------------------------------
  // SAD: completed / skipped habit is NOT failed (no penalty, no freeze probe).
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task CompletedOrSkipped_IsNotFailed_AndDoesNotConsumeFreeze()
  {
    var h = Build(freezeBalance: 5);
    var (habitId, hvId) = SeedScheduled(h);
    var date = new DateOnly(2026, 1, 1);

    // model a completed/skipped row: short-circuit freeze AND occupy the slot.
    h.Repo.CompletedOrSkipped.Add(hvId);
    h.Repo.ExistingExecutions.Add((hvId, date));
    // A seed exists, but the anti-join must exclude it.
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId };

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(0);
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
    // anyDone short-circuits before the freeze branch.
    h.Prot.TryConsumeFreezeCalls.Should().BeEmpty();
    h.Prot.FreezeBalance.Should().Be(5); // untouched
  }

  // ---------------------------------------------------------------------------
  // SAD: vacationed habit is NOT failed; a Vacation execution is inserted instead.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task Vacationed_IsNotFailed_AndInsertsVacationExecution()
  {
    var h = Build();
    var (habitId, hvId) = SeedScheduled(h);
    var date = new DateOnly(2026, 1, 1);
    h.Vac.WithActive(UserId, new DateOnly(2025, 12, 30), new DateOnly(2026, 1, 5));
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId };

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(1); // 1 vacation row inserted, 0 failed

    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Vacation)
      .Should().ContainSingle();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
  }

  // ---------------------------------------------------------------------------
  // SAD: frozen habit is NOT failed; freeze consumed and Freeze execution inserted.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task Frozen_IsNotFailed_ConsumesFreeze_AndInsertsFreezeExecution()
  {
    var h = Build(freezeBalance: 1);
    var (habitId, hvId) = SeedScheduled(h);
    var date = new DateOnly(2026, 1, 1);
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId };

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(1); // 1 freeze row inserted, 0 failed

    h.Prot.TryConsumeFreezeCalls.Should().ContainSingle();
    h.Prot.FreezeBalance.Should().Be(0); // decremented
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze)
      .Should().ContainSingle();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
  }

  // No freeze available -> the miss falls through to Fail (freeze does not rescue).
  [Fact]
  public async Task NoFreezeAvailable_Miss_FallsThroughToFail()
  {
    var h = Build(freezeBalance: 0);
    var (habitId, hvId) = SeedScheduled(h);
    var date = new DateOnly(2026, 1, 1);
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId };

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(1);
    h.Prot.TryConsumeFreezeCalls.Should().ContainSingle(); // probed
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze)
      .Should().BeEmpty(); // nothing inserted (consume returned false)
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();
    h.Penalty.EnqueuedRecords.Should().ContainSingle();
  }

  // ---------------------------------------------------------------------------
  // A user whose scheduled habits are already protected by a Vacation execution
  // must NOT have a freeze credit consumed. The freeze branch is skipped entirely
  // for users on vacation that date (Service.cs step 4): the vacation already
  // protects the day, so probing/consuming a freeze would only no-op on the
  // anti-join while burning a credit.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task VacationedUser_DoesNotConsumeFreeze()
  {
    var h = Build(freezeBalance: 1);
    var (habitId, hvId) = SeedScheduled(h);
    var date = new DateOnly(2026, 1, 1);
    h.Vac.WithActive(UserId, new DateOnly(2025, 12, 30), new DateOnly(2026, 1, 5));

    await h.Svc.MarkDailyFailures([habitId], date);

    // Correct behavior: the vacation already protects the day; freeze must be left intact.
    h.Prot.TryConsumeFreezeCalls.Should().BeEmpty();
    h.Prot.FreezeBalance.Should().Be(1);
  }

  // ---------------------------------------------------------------------------
  // Idempotent re-run: re-running over the same penaltyRepo / executionId does
  // not double-enqueue.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task IdempotentRerun_DoesNotDoubleEnqueuePenalty()
  {
    var h = Build();
    var (habitId, hvId) = SeedScheduled(h);
    var execId = Guid.NewGuid();
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = UserId, ExecutionId = execId };
    var date = new DateOnly(2026, 1, 1);

    var r1 = await h.Svc.MarkDailyFailures([habitId], date);
    ((int)r1).Should().Be(1);
    h.Penalty.EnqueuedRecords.Should().ContainSingle();

    // Second run: the slot is now occupied (ExistingExecutions), so CreateFailedExecutions
    // returns nothing; even if a row re-surfaced, EnqueuePending is idempotent on ExecutionId.
    var r2 = await h.Svc.MarkDailyFailures([habitId], date);
    ((int)r2).Should().Be(0);
    h.Penalty.EnqueuedRecords.Should().ContainSingle();
    h.Penalty.EnqueuedRecords[0].HabitExecutionId.Should().Be(execId);
  }

  // Two failed rows sharing one ExecutionId -> enqueued exactly once.
  [Fact]
  public async Task TwoFailedRowsSameExecutionId_EnqueuedExactlyOnce()
  {
    var h = Build();
    var (habitIdA, hvA) = SeedScheduled(h);
    var (habitIdB, hvB) = SeedScheduled(h);
    var execId = Guid.NewGuid();
    h.Repo.FailedRowSeeds[hvA] = new FailedRowSeed { UserId = UserId, ExecutionId = execId };
    h.Repo.FailedRowSeeds[hvB] = new FailedRowSeed { UserId = UserId, ExecutionId = execId };
    var date = new DateOnly(2026, 1, 1);

    var res = await h.Svc.MarkDailyFailures([habitIdA, habitIdB], date);

    ((int)res).Should().Be(2); // two failed rows counted
    h.Penalty.EnqueuedRecords.Should().ContainSingle(); // but only one penalty
  }

  // ---------------------------------------------------------------------------
  // ratio=0 / stake=0 -> failed row recorded, but NO penalty enqueued.
  // Parameterized so the skip-on-zero rule stays valid if amounts change.
  // ---------------------------------------------------------------------------
  [Theory]
  [InlineData(1000, 0)]     // ratio = 0
  [InlineData(0, 5000)]     // stake = 0
  [InlineData(0, 0)]        // both 0
  public async Task ZeroPenalty_FailsButDoesNotEnqueue(int stakeCents, int ratioBasisPoints)
  {
    var h = Build();
    var (habitId, hvId) = SeedScheduled(h);
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed
    {
      UserId = UserId,
      StakeCents = stakeCents,
      RatioBasisPoints = ratioBasisPoints
    };
    var date = new DateOnly(2026, 1, 1);

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(1); // the failed row still counts
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();
    h.Penalty.EnqueuedRecords.Should().BeEmpty(); // but no penalty
  }

  // Positive control for the theory: a non-zero stake/ratio DOES enqueue, with the
  // expected amount. Parameterized so amount math is locked across tier changes.
  [Theory]
  [InlineData(1000, 5000, 5.00)]   // $10 stake @ 50% -> $5.00
  [InlineData(2000, 2500, 5.00)]   // $20 stake @ 25% -> $5.00
  [InlineData(500, 10000, 5.00)]   // $5 stake @ 100% -> $5.00
  [InlineData(1234, 5000, 6.17)]   // 1234c * 5000 / 10000 = 617c -> $6.17
  public async Task NonZeroPenalty_EnqueuesExpectedAmount(int stakeCents, int ratioBasisPoints, decimal expectedDollars)
  {
    var h = Build();
    var (habitId, hvId) = SeedScheduled(h);
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed
    {
      UserId = UserId,
      StakeCents = stakeCents,
      RatioBasisPoints = ratioBasisPoints
    };
    var date = new DateOnly(2026, 1, 1);

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    ((int)res).Should().Be(1);
    h.Penalty.EnqueuedRecords.Should().ContainSingle();
    h.Penalty.EnqueuedRecords[0].Amount.Should()
      .Be(new Money(expectedDollars, Currency.FromCode("USD")));
  }

  // ---------------------------------------------------------------------------
  // Ordering: vacation insert -> freeze insert -> fail step (by call sequence).
  // The phases run globally (all vacations, then all freezes, then the single
  // fail step), so two distinct users exercise the ordering without the freeze
  // branch having to fire for a vacationing user (which it must not): userVac is
  // on vacation (vacation insert), userFrz has a freeze credit + a miss (freeze
  // insert). Comparing call sequence numbers proves the phase ordering.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task Ordering_VacationBeforeFreezeBeforeFail()
  {
    const string userVac = "user-vac";
    const string userFrz = "user-frz";

    var h = Build(freezeBalance: 1);
    var (vacHabitId, _) = SeedScheduled(h, userVac);
    var (frzHabitId, frzHvId) = SeedScheduled(h, userFrz);
    h.Vac.WithActive(userVac, new DateOnly(2025, 12, 30), new DateOnly(2026, 1, 5));
    h.Repo.FailedRowSeeds[frzHvId] = new FailedRowSeed { UserId = userFrz };
    var date = new DateOnly(2026, 1, 1);

    var res = await h.Svc.MarkDailyFailures([vacHabitId, frzHabitId], date);

    res.IsSuccess().Should().BeTrue();
    // 1 vacation row + 1 freeze row + 0 failed (both slots occupied before fail).
    ((int)res).Should().Be(2);

    var vacSeq = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Single(c => c.Status == ExecutionStatus.Vacation).Seq;
    var frzSeq = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Single(c => c.Status == ExecutionStatus.Freeze).Seq;
    var failSeq = h.Repo.CreateFailedExecutionsCalls.Single().Seq;

    vacSeq.Should().BeLessThan(frzSeq);
    frzSeq.Should().BeLessThan(failSeq);

    // The vacationing user did NOT consume a freeze; only userFrz did.
    h.Prot.TryConsumeFreezeCalls.Should().ContainSingle();
    h.Prot.TryConsumeFreezeCalls[0].UserId.Should().Be(userFrz);

    // Both days were protected, so no penalty is enqueued.
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
  }
}
