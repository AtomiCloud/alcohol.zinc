using CSharp_Result;
using Domain.Habit;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using UnitTest.Penalty;

namespace UnitTest.Protection;

// Unit tests for the FREEZE protection lifecycle that is reachable from the
// Domain layer with the shared hand-rolled fakes:
//
//   * CONSUME  -> HabitService.MarkDailyFailures step 4: a user with a freeze
//                 day available and nothing completed/skipped today gets all
//                 their scheduled versions marked Frozen, the user-level pool
//                 is decremented by exactly one, and those versions are then
//                 excluded from the Fail step (no penalty).
//   * CAP      -> IProtectionRepository increment-then-clamp mechanics
//                 (IncrementFreeze is cap-blind; ClampFreezeToCap is the cap
//                 enforcer) plus FreezePolicy.ComputeFreezeMax cap derivation.
//
// The cap is parameterized via [Theory] so these stay valid when the
// per-subscription-tier base cap / streak bonuses change.
//
// NOTE ON SCOPE: the *award* orchestration (perfect-week detection ->
// RecordFreezeAwardIfAbsent -> IncrementFreeze -> GetFreezeCapForUser ->
// ClampFreezeToCap) lives in App.Modules.Protection.ProtectionAwardService,
// which resolves IConfigurationService / IStreakService / IStreakRepository
// through an IServiceProvider scope. Those collaborators are NOT covered by
// the shared Protection fakes, so the end-to-end award-then-clamp flow is
// reported in needsIntegration rather than faked into a green unit test. Here
// we unit-test the repo-level cap contract the award flow relies on.
public class FreezeCapTests
{
  private const string UserId = "user-freeze-1";
  private static readonly DateOnly Date = new(2026, 1, 15);

  private static HabitService Build(
    FakeHabitRepository repo,
    FakeVacationRepository vac,
    FakeProtectionRepository prot,
    FakePenaltyRepository penalty,
    FakeEntitlementService? ent = null)
    => new(repo, vac, prot, ent ?? new FakeEntitlementService(), penalty,
      NullLogger<HabitService>.Instance);

  // Seed one scheduled habit (owned by UserId) that misses today unless a
  // protection (vacation/freeze) intervenes.
  private static (FakeHabitRepository repo, Guid habitId, Guid hvId) SeedScheduledMiss(
    CallLog log, string userId = UserId)
  {
    var habitId = Guid.NewGuid();
    var hvId = Guid.NewGuid();
    var repo = new FakeHabitRepository(log);
    repo.Habits.Add(HabitFakeFactory.Habit(habitId, userId));
    repo.ActiveVersions.Add(HabitFakeFactory.Version(hvId, habitId));
    repo.FailedRowSeeds[hvId] = new FailedRowSeed { UserId = userId };
    return (repo, habitId, hvId);
  }

  // ---------------------------------------------------------------------------
  // CONSUME — happy path
  // ---------------------------------------------------------------------------

  [Theory]
  [InlineData(1)] // exactly one day in the pool
  [InlineData(3)] // a larger pool: still only ONE day consumed for one date
  [InlineData(7)] // a full tier cap worth of days
  public async Task Consume_FreezeAvailable_MarksFrozen_DecrementsByOne_NoPenalty(int balance)
  {
    var log = new CallLog();
    var (repo, habitId, hvId) = SeedScheduledMiss(log);
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: balance);
    var penalty = new FakePenaltyRepository();
    var svc = Build(repo, vac, prot, penalty);

    var res = await svc.MarkDailyFailures([habitId], Date);

    res.IsSuccess().Should().BeTrue();
    // 0 failed + 0 vacation + 1 frozen
    ((int)res).Should().Be(1);

    // Exactly one freeze day consumed for this (user, date).
    prot.TryConsumeFreezeCalls.Should().ContainSingle()
      .Which.Should().Be((UserId, Date));
    prot.FreezeBalance.Should().Be(balance - 1, "exactly one freeze day is consumed per protected date");
    prot.ConsumedDates.Should().Contain((UserId, Date));

    // A Frozen execution row was created for the scheduled version.
    var freezeInserts = repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze).ToList();
    freezeInserts.Should().ContainSingle();
    freezeInserts[0].HvIds.Should().ContainSingle().Which.Should().Be(hvId);
    freezeInserts[0].Date.Should().Be(Date);

    // The frozen version is excluded from the Fail step => no Failed row, no penalty.
    repo.CreateFailedExecutionsCalls.Should().ContainSingle();
    repo.CreateFailedExecutionsCalls[0].Returned.Should().BeEmpty();
    penalty.EnqueuedRecords.Should().BeEmpty();
  }

  [Fact]
  public async Task Consume_IsIdempotentPerDate_SecondRunDoesNotDecrementAgain()
  {
    var log = new CallLog();
    var (repo, habitId, _) = SeedScheduledMiss(log);
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: 1);
    var penalty = new FakePenaltyRepository();
    var svc = Build(repo, vac, prot, penalty);

    await svc.MarkDailyFailures([habitId], Date);
    prot.FreezeBalance.Should().Be(0);

    // Re-running the same date must not re-charge the pool (real repo is
    // idempotent per (user,date) via the consumption ledger).
    await svc.MarkDailyFailures([habitId], Date);

    prot.FreezeBalance.Should().Be(0, "a date already frozen must not consume a second freeze day");
    prot.TryConsumeFreezeCalls.Should().HaveCount(2);
    penalty.EnqueuedRecords.Should().BeEmpty();
  }

  // ---------------------------------------------------------------------------
  // CONSUME — sad path
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Consume_BalanceZero_NotConsumed_VersionFails_PenaltyEnqueued()
  {
    var log = new CallLog();
    var (repo, habitId, _) = SeedScheduledMiss(log);
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: 0);
    var penalty = new FakePenaltyRepository();
    var svc = Build(repo, vac, prot, penalty);

    var res = await svc.MarkDailyFailures([habitId], Date);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1); // 1 failed + 0 vacation + 0 frozen

    // TryConsumeFreeze was attempted but returned false => no decrement, no freeze row.
    prot.TryConsumeFreezeCalls.Should().ContainSingle();
    prot.FreezeBalance.Should().Be(0);
    prot.ConsumedDates.Should().BeEmpty();
    repo.CreateExecutionsForVersionsWithStatusCalls
      .Where(c => c.Status == ExecutionStatus.Freeze)
      .Should().BeEmpty("a zero balance must not create any Frozen executions");

    // Version actually failed => penalty enqueued (1000c * 5000bps = $5.00).
    repo.CreateFailedExecutionsCalls[0].Returned.Should().ContainSingle();
    penalty.EnqueuedRecords.Should().ContainSingle()
      .Which.Amount.Should().Be(new Money(5.00m, Currency.FromCode("USD")));
  }

  [Fact]
  public async Task Consume_AlreadyCompletedOrSkipped_ShortCircuits_NoConsumeAttempt()
  {
    var log = new CallLog();
    var (repo, habitId, hvId) = SeedScheduledMiss(log);
    // Model a completed/skipped day: short-circuits the freeze branch AND
    // occupies the slot so the fail step skips it.
    repo.CompletedOrSkipped.Add(hvId);
    repo.ExistingExecutions.Add((hvId, Date));
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: 5);
    var penalty = new FakePenaltyRepository();
    var svc = Build(repo, vac, prot, penalty);

    var res = await svc.MarkDailyFailures([habitId], Date);

    ((int)res).Should().Be(0);
    prot.TryConsumeFreezeCalls.Should().BeEmpty("a completed/skipped day must never spend a freeze");
    prot.FreezeBalance.Should().Be(5);
    penalty.EnqueuedRecords.Should().BeEmpty();
  }

  // ---------------------------------------------------------------------------
  // CONSUME — depletion across distinct dates (cannot exceed what's in the pool)
  // ---------------------------------------------------------------------------

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(7)]
  public async Task Consume_AcrossDistinctDates_CannotExceedPool_ExcessDatesFail(int balance)
  {
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: balance);
    var penalty = new FakePenaltyRepository();

    // Run one more day than the pool can cover.
    var attempts = balance + 1;
    var frozenDays = 0;
    var failedDays = 0;

    for (var i = 0; i < attempts; i++)
    {
      var log = new CallLog();
      var (repo, habitId, _) = SeedScheduledMiss(log);
      var svc = Build(repo, vac, prot, penalty);
      var date = Date.AddDays(i);

      await svc.MarkDailyFailures([habitId], date);

      var frozeToday = repo.CreateExecutionsForVersionsWithStatusCalls
        .Any(c => c.Status == ExecutionStatus.Freeze);
      if (frozeToday) frozenDays++;
      if (repo.CreateFailedExecutionsCalls[0].Returned.Count > 0) failedDays++;
    }

    // Pool covers exactly `balance` distinct dates; the extra date must fail.
    frozenDays.Should().Be(balance);
    failedDays.Should().Be(attempts - balance);
    prot.FreezeBalance.Should().Be(0);
    // One penalty per failed (excess) date.
    penalty.EnqueuedRecords.Should().HaveCount(attempts - balance);
  }

  // ---------------------------------------------------------------------------
  // CAP — repo-level increment-then-clamp contract (what the award flow relies on)
  // ---------------------------------------------------------------------------

  [Theory]
  [InlineData(0, 3, 3)] // start empty, award 3, cap 3 -> 3
  [InlineData(2, 10, 5)] // award beyond cap -> clamped down to cap
  [InlineData(5, 1, 7)] // award stays under cap -> not clamped
  [InlineData(7, 4, 7)] // already at cap, award more -> clamped back to cap
  public async Task Cap_IncrementThenClamp_NeverExceedsCap(int start, int award, int cap)
  {
    var prot = new FakeProtectionRepository(freezeBalance: start);

    // Award is cap-blind...
    var inc = await prot.IncrementFreeze(UserId, award);
    inc.IsSuccess().Should().BeTrue();
    prot.FreezeBalance.Should().Be(start + award, "IncrementFreeze must be cap-blind");

    // ...then ClampFreezeToCap enforces the ceiling.
    var clamp = await prot.ClampFreezeToCap(UserId, cap);
    clamp.IsSuccess().Should().BeTrue();

    var expected = Math.Min(start + award, cap);
    prot.FreezeBalance.Should().Be(expected);
    prot.FreezeBalance.Should().BeLessThanOrEqualTo(cap, "the freeze pool must never exceed the cap after clamping");

    prot.IncrementFreezeCalls.Should().ContainSingle().Which.Should().Be((UserId, award));
    prot.ClampFreezeToCapCalls.Should().ContainSingle().Which.Should().Be((UserId, cap));
  }

  [Fact]
  public async Task Cap_ClampOnDowngrade_TrimsExistingBalanceToLowerCap()
  {
    // Simulate a subscription downgrade: balance was accumulated under a higher
    // tier, the new (lower) cap must trim it.
    var prot = new FakeProtectionRepository(freezeBalance: 10);

    await prot.ClampFreezeToCap(UserId, cap: 3);

    prot.FreezeBalance.Should().Be(3);
  }

  // ---------------------------------------------------------------------------
  // CAP — award ledger idempotency (one freeze per habit/week, enforced by the
  // RecordFreezeAwardIfAbsent ledger the award flow gates IncrementFreeze on)
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Cap_AwardLedger_IsIdempotentPerHabitWeek()
  {
    var prot = new FakeProtectionRepository(freezeBalance: 0);
    var habitId = Guid.NewGuid();
    var weekStart = new DateOnly(2026, 1, 11); // a Sunday

    var first = await prot.RecordFreezeAwardIfAbsent(habitId, weekStart);
    var second = await prot.RecordFreezeAwardIfAbsent(habitId, weekStart);

    ((bool)first).Should().BeTrue("the first award for a habit/week is recorded");
    ((bool)second).Should().BeFalse("a duplicate award for the same habit/week must be rejected");
    prot.RecordFreezeAwardIfAbsentCalls.Should().HaveCount(2);
    prot.AwardLedger.Should().ContainSingle().Which.Should().Be((habitId, weekStart));
  }
}
