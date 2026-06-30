using CSharp_Result;
using Domain.Habit;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using UnitTest.Penalty;

namespace UnitTest.Protection;

// Unit tests for the VACATION-pause branch of HabitService.MarkDailyFailures.
//
// Contract under test (Service.cs steps 3 + 5):
//   For a habit scheduled today whose owner has >=1 active vacation window on
//   that date, the service pre-inserts a Vacation execution
//   (CreateExecutionsForVersionsWithStatus(..., Vacation)) BEFORE the fail step.
//   The pre-insert occupies the (hvId, date) slot, so the CreateFailedExecutions
//   anti-join skips it: the habit is NOT Failed and no penalty is enqueued.
//
// NOTE on date/timezone/boundary resolution: HabitService only reads
// vacations.Count for the queried date. WHICH windows are "active on date" — the
// start/end inclusivity, multi-day spans, timezone-shifted day boundaries and
// window overlap — is entirely decided by IVacationRepository.ListActiveForUserOnDate
// (a SQL query). None of that logic lives in HabitService, so it cannot be
// exercised with a fake that just returns a seeded .Count. Those edge cases are
// reported in needsIntegration rather than faked into green here.
public class VacationPauseTests
{
  private const string UserId = "user-vac-1";

  private sealed record Harness(
    FakeHabitRepository Repo,
    FakeVacationRepository Vac,
    FakeProtectionRepository Prot,
    FakeEntitlementService Ent,
    FakePenaltyRepository Penalty,
    HabitService Svc);

  // freezeBalance defaults to 0 so the freeze branch never fires unless a test
  // opts in: vacation behaviour must be isolated from the freeze branch.
  private static Harness Build(int freezeBalance = 0)
  {
    var log = new CallLog();
    var repo = new FakeHabitRepository(log);
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance);
    var ent = new FakeEntitlementService();
    var penalty = new FakePenaltyRepository();
    var svc = new HabitService(repo, vac, prot, ent, penalty, NullLogger<HabitService>.Instance);
    return new Harness(repo, vac, prot, ent, penalty, svc);
  }

  // Seed one scheduled habit + version that would FAIL absent any protection.
  private static (Guid HabitId, Guid HvId, Guid ExecutionId) SeedScheduledMiss(
    Harness h, string userId = UserId)
  {
    var habitId = Guid.NewGuid();
    var hvId = Guid.NewGuid();
    var execId = Guid.NewGuid();
    h.Repo.Habits.Add(HabitFakeFactory.Habit(habitId, userId));
    h.Repo.ActiveVersions.Add(HabitFakeFactory.Version(hvId, habitId));
    h.Repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = userId, ExecutionId = execId };
    return (habitId, hvId, execId);
  }

  // -------------------------------------------------------------------------
  // HAPPY PATH: active vacation pauses the habit.
  // -------------------------------------------------------------------------

  [Fact]
  public async Task ActiveVacation_SchedulesVacationExecution_AndDoesNotFail()
  {
    var h = Build();
    var (habitId, hvId, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();

    // A Vacation execution was inserted for the scheduled version.
    var vacationInserts = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Vacation).ToList();
    vacationInserts.Should().ContainSingle();
    vacationInserts[0].HvIds.Should().Contain(hvId);
    vacationInserts[0].Date.Should().Be(date);

    // It was NOT failed: the fail step found nothing to fail (slot occupied).
    h.Repo.CreateFailedExecutionsCalls.Should().ContainSingle();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();

    // No penalty enqueued because nothing failed.
    h.Penalty.EnqueuedRecords.Should().BeEmpty();

    // Return value counts the protected (vacation) row, not a failure.
    ((int)res).Should().Be(1);
  }

  [Fact]
  public async Task ActiveVacation_DoesNotConsumeFreeze()
  {
    // Vacation runs BEFORE the freeze branch and the freeze branch is SKIPPED for
    // a user on vacation that date: the vacation insert already protects every
    // scheduled habit, so consuming a freeze would only no-op on the anti-join
    // while burning a credit. Even with freeze available, none must be consumed.
    var h = Build(freezeBalance: 5);
    var (habitId, hvId, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();

    // Vacation insert happened.
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Count(c => c.Status == ExecutionStatus.Vacation).Should().Be(1);

    // The freeze branch was skipped: no freeze probed, none consumed, no insert.
    h.Prot.TryConsumeFreezeCalls.Should().BeEmpty();
    h.Prot.BalanceFor(UserId).Should().Be(5); // untouched
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze).Should().BeEmpty();

    // Not failed, no penalty.
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
  }

  [Fact]
  public async Task ActiveVacation_OrdersVacationInsertBeforeFailStep()
  {
    var h = Build();
    var (habitId, _, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    await h.Svc.MarkDailyFailures([habitId], date);

    var vacationSeq = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Single(c => c.Status == ExecutionStatus.Vacation).Seq;
    var failSeq = h.Repo.CreateFailedExecutionsCalls.Single().Seq;

    vacationSeq.Should().BeLessThan(failSeq,
      "the Vacation pre-insert must occupy the slot before the fail anti-join runs");
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public async Task MultipleVacationWindows_SamePauseOutcome(int windowCount)
  {
    // The service only reads vacations.Count > 0, so 1..N overlapping windows
    // all yield the identical pause. Parameterized so it stays valid if window
    // limits / overlap rules change at the repository layer.
    var h = Build();
    var (habitId, _, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    for (var i = 0; i < windowCount; i++)
      h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Count(c => c.Status == ExecutionStatus.Vacation).Should().Be(1);
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
  }

  [Fact]
  public async Task VacationPausesAllScheduledHabitsForUser_OnSameDay()
  {
    var h = Build();
    var a = SeedScheduledMiss(h);
    var b = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    var res = await h.Svc.MarkDailyFailures([a.HabitId, b.HabitId], date);

    res.IsSuccess().Should().BeTrue();

    var vacationInsert = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Single(c => c.Status == ExecutionStatus.Vacation);
    vacationInsert.HvIds.Should().BeEquivalentTo([a.HvId, b.HvId]);

    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
    ((int)res).Should().Be(2); // two vacation rows inserted
  }

  // -------------------------------------------------------------------------
  // SAD PATH: no active vacation -> habit fails normally.
  // -------------------------------------------------------------------------

  [Fact]
  public async Task NoActiveVacation_HabitFails_AndPenaltyEnqueued()
  {
    var h = Build();
    var (habitId, _, execId) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    // No vacation seeded for UserId.

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();

    // No vacation inserts at all.
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Vacation).Should().BeEmpty();

    // The habit was failed.
    h.Repo.CreateFailedExecutionsCalls.Should().ContainSingle();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();

    // Penalty enqueued for the failed execution.
    h.Penalty.EnqueuedRecords.Should().ContainSingle();
    h.Penalty.EnqueuedRecords[0].HabitExecutionId.Should().Be(execId);
    h.Penalty.EnqueuedRecords[0].Status.Should().Be(Domain.Penalty.PenaltyStatus.Pending);
    h.Penalty.EnqueuedRecords[0].Amount.Should()
      .Be(new Money(5.00m, Currency.FromCode("USD"))); // 1000c * 5000bps / 10000
    ((int)res).Should().Be(1); // one failure
  }

  [Fact]
  public async Task VacationForDifferentUser_DoesNotPauseThisUsersHabit()
  {
    var h = Build();
    var (habitId, _, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    // Vacation belongs to an unrelated user; ListActiveForUserOnDate is keyed by
    // userId, so this user's habit must still fail.
    h.Vac.WithActive("some-other-user", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();
    h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Vacation).Should().BeEmpty();
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();
    h.Penalty.EnqueuedRecords.Should().ContainSingle();
  }

  [Fact]
  public async Task VacationQueriedForExactMarkDate()
  {
    // The vacation lookup must use the SAME date being marked (no off-by-one).
    var h = Build();
    var (habitId, _, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

    await h.Svc.MarkDailyFailures([habitId], date);

    h.Vac.ListActiveForUserOnDateCalls.Should().ContainSingle();
    h.Vac.ListActiveForUserOnDateCalls[0].UserId.Should().Be(UserId);
    h.Vac.ListActiveForUserOnDateCalls[0].Date.Should().Be(date);
  }

  [Fact]
  public async Task VacationInsertIsIdempotent_WhenSlotAlreadyExists()
  {
    // Model a pre-existing execution row for (hvId, date) — e.g. a Vacation row
    // already written by an earlier pass within the after-midnight window. The
    // re-run must not double-fail and must not enqueue a penalty.
    var h = Build();
    var (habitId, hvId, _) = SeedScheduledMiss(h);
    var date = new DateOnly(2026, 6, 10);
    h.Vac.WithActive(UserId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
    h.Repo.ExistingExecutions.Add((hvId, date)); // slot already taken

    var res = await h.Svc.MarkDailyFailures([habitId], date);

    res.IsSuccess().Should().BeTrue();
    // Vacation insert is a no-op (0 new rows) because the slot exists.
    var vacInserts = h.Repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Vacation).ToList();
    vacInserts.Should().ContainSingle();
    // Nothing failed, nothing enqueued.
    h.Repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    h.Penalty.EnqueuedRecords.Should().BeEmpty();
    ((int)res).Should().Be(0); // 0 inserted (no-op) + 0 frozen + 0 failed
  }
}
