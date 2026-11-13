using CSharp_Result;
using Domain.Habit;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using UnitTest.Penalty;

namespace UnitTest.Protection;

// Unit tests for the FREEZE protection lifecycle that is reachable from the
// Domain layer with the shared hand-rolled fakes:
//
//   * CONSUME  -> covered DB-backed in IntTest/Check/CheckScenarioTests.cs
//                 (CheckService.CloseDue: one freeze per user per day).
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

  // ---------------------------------------------------------------------------
  // CONSUME — happy path
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
    prot.BalanceFor(UserId).Should().Be(start + award, "IncrementFreeze must be cap-blind");

    // ...then ClampFreezeToCap enforces the ceiling.
    var clamp = await prot.ClampFreezeToCap(UserId, cap);
    clamp.IsSuccess().Should().BeTrue();

    var expected = Math.Min(start + award, cap);
    prot.BalanceFor(UserId).Should().Be(expected);
    prot.BalanceFor(UserId).Should().BeLessThanOrEqualTo(cap, "the freeze pool must never exceed the cap after clamping");

    prot.IncrementFreezeCalls.Should().ContainSingle().Which.Should().Be((UserId, award));
    prot.ClampFreezeToCapCalls.Should().ContainSingle().Which.Should().Be((UserId, cap));
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

  // ---------------------------------------------------------------------------
  // CAP — FreezePolicy: free (base 0) earns nothing, not even streak bonuses
  // ---------------------------------------------------------------------------

  [Theory]
  [InlineData(0, 0)]
  [InlineData(0, 30)]  // first streak milestone
  [InlineData(0, 450)] // far past every milestone
  public void FreezeMax_TierWithoutFreezes_IsZeroAtAnyStreak(int baseCap, int maxStreak)
  {
    new App.Modules.Protection.FreezePolicy().ComputeFreezeMax(baseCap, maxStreak).Should().Be(0);
  }

  [Theory]
  [InlineData(14, 29, 14)] // just under the first milestone
  [InlineData(14, 30, 15)]
  [InlineData(14, 80, 16)]
  [InlineData(30, 300, 34)] // 30 + 3 milestones + 1 per extra 150 days
  public void FreezeMax_PaidTier_KeepsStreakBonuses(int baseCap, int maxStreak, int expected)
  {
    new App.Modules.Protection.FreezePolicy().ComputeFreezeMax(baseCap, maxStreak).Should().Be(expected);
  }
}
