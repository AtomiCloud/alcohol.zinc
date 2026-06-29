using App.Error;
using App.Modules.Entitlement;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Habit;
using Domain.Protection;
using Domain.Subscription;
using Domain.Vacation;
using Microsoft.Extensions.Logging.Abstractions;
using UnitTest.Penalty;

namespace UnitTest.Protection;

// Tests for SKIP CRUD (the HabitService.SkipHabit happy path) and for the
// MONTHLY SKIP LIMIT enforcement.
//
// IMPORTANT architectural note discovered while writing these tests:
//   * Domain.Habit.HabitService.SkipHabit does NOT enforce any monthly skip
//     limit. It simply resolves the user-local date and delegates to
//     repo.SkipHabit. So a "skip beyond the limit" CANNOT be rejected at the
//     domain-service layer — the cap is enforced one layer up.
//   * The actual cap lives in App.Modules.Entitlement.EntitlementService
//     .EnsureSkipsAllowed, which the HabitController calls BEFORE SkipHabit:
//         GetUserMonthWindow -> EnsureSkipsAllowed -> SkipHabit
//     EnsureSkipsAllowed compares CountUserSkipsForMonth(used) against the
//     tier limit (EntitlementKeys.SkipsMonthly) and fails with a
//     TierInsufficient problem when used >= limit.
//
// Therefore the HAPPY path (skip succeeds) and SAD path (skip beyond limit
// rejected) are unit-tested at the layer that actually owns each behaviour:
//   - SkipHabit happy path        -> HabitService (delegation + correct date)
//   - skip-within-limit allowed   -> EntitlementService.EnsureSkipsAllowed
//   - skip-beyond-limit rejected  -> EntitlementService.EnsureSkipsAllowed
//
// The end-to-end controller wiring (Guard -> month window -> EnsureSkipsAllowed
// -> SkipHabit short-circuiting before the repo write) is integration-only; see
// needsIntegration in the report.
public class SkipLimitTests
{
  private const string UserId = "user-1";

  private static HabitService BuildHabitService(FakeHabitRepository repo)
  {
    var vac = new FakeVacationRepository();
    var prot = new FakeProtectionRepository(freezeBalance: 0);
    var ent = new FakeEntitlementService();
    var penaltyRepo = new FakePenaltyRepository();
    return new HabitService(repo, vac, prot, ent, penaltyRepo, NullLogger<HabitService>.Instance);
  }

  // ---------------------------------------------------------------------------
  // SKIP CRUD — happy path on the domain service.
  // SkipHabit must resolve the user-local current date and delegate to the repo
  // with exactly the userId/version/date/notes it was given.
  // ---------------------------------------------------------------------------
  [Fact]
  public async Task SkipHabit_HappyPath_DelegatesToRepoWithUserLocalDate()
  {
    var hvId = Guid.NewGuid();
    var today = new DateOnly(2026, 6, 7);
    var repo = new FakeHabitRepository { CurrentDate = today };
    var svc = BuildHabitService(repo);

    var res = await svc.SkipHabit(UserId, hvId, "felt sick");

    res.IsSuccess().Should().BeTrue();
    var exec = res.SuccessOrDefault()!;
    exec.HabitVersionId.Should().Be(hvId);
    exec.Record.Status.Should().Be(ExecutionStatus.Skipped);
    exec.Record.Date.Should().Be(today);
    exec.Record.Notes.Should().Be("felt sick");

    repo.SkipHabitCalls.Should().ContainSingle();
    repo.SkipHabitCalls[0].Should().Be((UserId, hvId, today, "felt sick"));
  }

  [Fact]
  public async Task SkipHabit_PassesNullNotesThrough()
  {
    var hvId = Guid.NewGuid();
    var repo = new FakeHabitRepository { CurrentDate = new DateOnly(2026, 6, 7) };
    var svc = BuildHabitService(repo);

    var res = await svc.SkipHabit(UserId, hvId, null);

    res.IsSuccess().Should().BeTrue();
    repo.SkipHabitCalls.Should().ContainSingle();
    repo.SkipHabitCalls[0].Notes.Should().BeNull();
  }

  // ---------------------------------------------------------------------------
  // MONTHLY SKIP LIMIT — happy path: under the cap, EnsureSkipsAllowed succeeds.
  // Parameterised over (limit, used) so the assertions stay valid as per-tier
  // limits change.
  // ---------------------------------------------------------------------------
  [Theory]
  [InlineData(5, 0)]   // none used yet
  [InlineData(5, 4)]   // last allowed skip (used < limit)
  [InlineData(1, 0)]   // tier with a single monthly skip, none used
  [InlineData(30, 29)] // higher tier, last allowed skip
  public async Task EnsureSkipsAllowed_UnderLimit_Succeeds(int limit, int used)
  {
    var (start, end) = MonthWindow();
    var sub = new FakeSubscriptionService("free", limit);
    var habitRepo = new FakeSkipCountRepository(used);
    var svc = BuildEntitlement(sub, habitRepo);

    var res = await svc.EnsureSkipsAllowed(UserId, start, end);

    res.IsSuccess().Should().BeTrue();
    habitRepo.CountUserSkipsForMonthCalls.Should().ContainSingle();
    habitRepo.CountUserSkipsForMonthCalls[0].Should().Be((UserId, start, end));
    sub.GetLimitForTierCalls.Should().Contain((("free"), EntitlementKeys.SkipsMonthly));
  }

  // ---------------------------------------------------------------------------
  // MONTHLY SKIP LIMIT — sad path: at/over the cap, EnsureSkipsAllowed must
  // REJECT (used >= limit). This asserts the CORRECT behaviour: a user must NOT
  // be able to skip beyond the monthly limit.
  // ---------------------------------------------------------------------------
  [Theory]
  [InlineData(5, 5)]    // exactly at the cap
  [InlineData(5, 6)]    // over the cap (defensive)
  [InlineData(1, 1)]    // single-skip tier, already used
  [InlineData(0, 0)]    // tier with zero skips: never allowed
  [InlineData(30, 30)]  // higher tier at cap
  public async Task EnsureSkipsAllowed_AtOrOverLimit_IsRejected(int limit, int used)
  {
    var (start, end) = MonthWindow();
    var sub = new FakeSubscriptionService("free", limit);
    var habitRepo = new FakeSkipCountRepository(used);
    var svc = BuildEntitlement(sub, habitRepo);

    var res = await svc.EnsureSkipsAllowed(UserId, start, end);

    res.IsFailure().Should().BeTrue();
    var err = res.FailureOrDefault();
    err.Should().BeOfType<DomainProblemException>();
    var problem = ((DomainProblemException)err!).Problem;
    problem.Should().BeOfType<App.Error.V1.TierInsufficient>();
    var tier = (App.Error.V1.TierInsufficient)problem;
    tier.LimitKey.Should().Be(EntitlementKeys.SkipsMonthly);
    tier.LimitValue.Should().Be(limit);
    tier.Tier.Should().Be("free");
  }

  // ---------------------------------------------------------------------------
  // helpers
  // ---------------------------------------------------------------------------
  private static EntitlementService BuildEntitlement(
    FakeSubscriptionService sub, FakeSkipCountRepository habitRepo)
    => new(sub, new FakeVacationRepository(), habitRepo, new NoopFreezePolicy());

  private static (DateOnly Start, DateOnly End) MonthWindow()
    => (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
}

// ---------------------------------------------------------------------------
// Local minimal fakes for the EntitlementService dependencies that the SHARED
// Fakes.cs does not back (the shared FakeHabitRepository throws on
// CountUserSkipsForMonth, and there is no subscription / freeze-policy fake).
// Kept here, namespace-local, so the shared file stays untouched.
// ---------------------------------------------------------------------------

internal sealed class FakeSubscriptionService(string tier, int skipsMonthlyLimit) : ISubscriptionService
{
  public List<(string Tier, string Key)> GetLimitForTierCalls { get; } = [];

  public Task<Result<string>> GetUserTier(string userId)
    => Task.FromResult<Result<string>>(tier);

  public Task<Result<int>> GetLimitForTier(string t, string key)
  {
    GetLimitForTierCalls.Add((t, key));
    var limit = key == EntitlementKeys.SkipsMonthly ? skipsMonthlyLimit : 0;
    return Task.FromResult<Result<int>>(limit);
  }
}

internal sealed class NoopFreezePolicy : IFreezePolicy
{
  public int ComputeFreezeMax(int baseCap, int userMaxStreak) => baseCap;
}

// Minimal IHabitRepository that only backs the skip-count query
// EnsureSkipsAllowed uses; everything else throws so misuse is loud.
internal sealed class FakeSkipCountRepository(int usedSkips) : IHabitRepository
{
  public List<(string UserId, DateOnly Start, DateOnly End)> CountUserSkipsForMonthCalls { get; } = [];

  public Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd)
  {
    CountUserSkipsForMonthCalls.Add((userId, monthStart, monthEnd));
    return Task.FromResult<Result<int>>(usedSkips);
  }

  // ---- unused members ----
  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date) => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch) => throw new NotImplementedException();
  public Task<Result<HabitPrincipal?>> GetHabit(Guid habitId) => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId) => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord) => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled) => throw new NotImplementedException();
  public Task<Result<Unit?>> Delete(Guid habitId, string userId) => throw new NotImplementedException();
  public Task<Result<List<FailedExecutionRow>>> CreateFailedExecutions(List<Guid> habitIds, DateOnly date) => throw new NotImplementedException();
  public Task<Result<int>> CreateExecutionsForVersionsWithStatus(List<Guid> habitVersionIds, DateOnly date, ExecutionStatus status) => throw new NotImplementedException();
  public Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId) => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes) => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, DateOnly date, string? notes) => throw new NotImplementedException();
  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch) => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId) => throw new NotImplementedException();
  public Task<Result<int>> CountHabitsForUser(string userId) => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersionsByIds(List<Guid> habitIds, DateOnly date) => throw new NotImplementedException();
  public Task<Result<List<HabitPrincipal>>> GetHabitsByIds(List<Guid> habitIds) => throw new NotImplementedException();
  public Task<Result<bool>> HasAnyCompletedOrSkippedForVersions(List<Guid> habitVersionIds, DateOnly date) => throw new NotImplementedException();
  public Task<Result<List<string>>> GetDistinctTimezonesForEnabledHabits() => throw new NotImplementedException();
  public Task<Result<List<Guid>>> GetEnabledHabitIdsByTimezone(string timezone) => throw new NotImplementedException();
}
