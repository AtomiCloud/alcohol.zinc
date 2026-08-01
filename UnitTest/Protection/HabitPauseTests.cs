using App.Error;
using App.Modules.Entitlement;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Habit;
using Domain.Subscription;

namespace UnitTest.Protection;

// Tests for OVER-CAP HABIT PAUSING at the layer that owns each behaviour:
//   - ReconcileHabitPause resolves the target tier's habit cap and delegates
//     the whole re-rank to the repository (SetPausedOverCap).
//   - EnsureHabitNotPaused / EnsureHabitVersionNotPaused make paused habits
//     read-only: writes are rejected with a TierInsufficient problem, not a 404.
// The SQL ranking itself (active-first, oldest-first, Id tiebreak) lives in
// HabitRepository.SetPausedOverCap and is integration-only.
public class HabitPauseTests
{
  private const string UserId = "user-1";

  private static EntitlementService Build(FakeHabitCapSubscriptionService sub, FakePausedHabitRepository repo)
    => new(sub, new FakeVacationRepository(), repo, new NoopFreezePolicy());

  [Fact]
  public async Task ReconcileHabitPause_ResolvesCapForTargetTier_AndDelegatesToRepo()
  {
    var sub = new FakeHabitCapSubscriptionService("free", 2);
    var repo = new FakePausedHabitRepository { SetPausedOverCapResult = 3 };

    var res = await Build(sub, repo).ReconcileHabitPause(UserId, "free");

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(3);
    sub.GetLimitForTierCalls.Should().Contain(("free", EntitlementKeys.HabitsMax),
      "the cap comes from the TARGET tier, not the user's current one");
    repo.SetPausedOverCapCalls.Should().ContainSingle().Which.Should().Be((UserId, 2));
  }

  [Theory]
  [InlineData(false)] // active habit: writes pass through
  [InlineData(true)]  // missing habit (null): the repo write owns the 404
  public async Task EnsureHabitNotPaused_ActiveOrMissing_Succeeds(bool missing)
  {
    var sub = new FakeHabitCapSubscriptionService("free", 2);
    var repo = new FakePausedHabitRepository { Paused = missing ? null : false };

    (await Build(sub, repo).EnsureHabitNotPaused(UserId, Guid.NewGuid())).IsSuccess().Should().BeTrue();
    (await Build(sub, repo).EnsureHabitVersionNotPaused(UserId, Guid.NewGuid())).IsSuccess().Should().BeTrue();
  }

  [Fact]
  public async Task EnsureHabitNotPaused_Paused_RejectsWithTierInsufficient()
  {
    var sub = new FakeHabitCapSubscriptionService("free", 2);
    var repo = new FakePausedHabitRepository { Paused = true };

    var res = await Build(sub, repo).EnsureHabitNotPaused(UserId, Guid.NewGuid());

    res.IsFailure().Should().BeTrue();
    var problem = ((DomainProblemException)res.FailureOrDefault()!).Problem;
    var tier = problem.Should().BeOfType<App.Error.V1.TierInsufficient>().Subject;
    tier.LimitKey.Should().Be(EntitlementKeys.HabitsMax);
    tier.LimitValue.Should().Be(2);
    tier.Tier.Should().Be("free");
  }

  [Fact]
  public async Task EnsureHabitVersionNotPaused_Paused_RejectsWithTierInsufficient()
  {
    var sub = new FakeHabitCapSubscriptionService("free", 2);
    var repo = new FakePausedHabitRepository { Paused = true };

    var res = await Build(sub, repo).EnsureHabitVersionNotPaused(UserId, Guid.NewGuid());

    res.IsFailure().Should().BeTrue();
    ((DomainProblemException)res.FailureOrDefault()!).Problem
      .Should().BeOfType<App.Error.V1.TierInsufficient>();
  }
}

// ISubscriptionService fake answering the HABIT cap (the shared
// FakeSubscriptionService in SkipLimitTests only answers the skips key).
internal sealed class FakeHabitCapSubscriptionService(string tier, int habitsMax) : ISubscriptionService
{
  public List<(string Tier, string Key)> GetLimitForTierCalls { get; } = [];

  public Task<Result<string>> GetUserTier(string userId)
    => Task.FromResult<Result<string>>(tier);

  public Task<Result<int>> GetLimitForTier(string t, string key)
  {
    GetLimitForTierCalls.Add((t, key));
    return Task.FromResult<Result<int>>(key == EntitlementKeys.HabitsMax ? habitsMax : 0);
  }
}

// Minimal IHabitRepository backing only the paused-flag lookups and the
// reconcile delegate; everything else throws so misuse is loud.
internal sealed class FakePausedHabitRepository : IHabitRepository
{
  public bool? Paused { get; set; }
  public Result<int> SetPausedOverCapResult { get; set; } = 0;
  public List<(string UserId, int Cap)> SetPausedOverCapCalls { get; } = [];

  public Task<Result<int>> SetPausedOverCap(string userId, int cap)
  {
    SetPausedOverCapCalls.Add((userId, cap));
    return Task.FromResult(SetPausedOverCapResult);
  }

  public Task<Result<bool?>> GetPausedByHabitId(string userId, Guid habitId)
    => Task.FromResult<Result<bool?>>(Paused);
  public Task<Result<bool?>> GetPausedByVersionId(string userId, Guid habitVersionId)
    => Task.FromResult<Result<bool?>>(Paused);

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
  public Task<Result<string?>> GetTaskNameByExecutionId(Guid executionId) => throw new NotImplementedException();
  public Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId) => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes) => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, DateOnly date, string? notes) => throw new NotImplementedException();
  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch) => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId) => throw new NotImplementedException();
  public Task<Result<int>> CountHabitsForUser(string userId) => throw new NotImplementedException();
  public Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd) => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersionsByIds(List<Guid> habitIds, DateOnly date) => throw new NotImplementedException();
  public Task<Result<List<HabitPrincipal>>> GetHabitsByIds(List<Guid> habitIds) => throw new NotImplementedException();
  public Task<Result<bool>> HasAnyCompletedOrSkippedForVersions(List<Guid> habitVersionIds, DateOnly date) => throw new NotImplementedException();
  public Task<Result<List<string>>> GetDistinctTimezonesForEnabledHabits() => throw new NotImplementedException();
  public Task<Result<List<Guid>>> GetEnabledHabitIdsByTimezone(string timezone) => throw new NotImplementedException();
}
