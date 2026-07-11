using CSharp_Result;
using Domain.Habit;
using Domain.NfcTag;

namespace UnitTest.NfcTag;

// Hand-rolled fakes implementing the real contracts (no Moq, matching the
// UnitTest/Handoff style) for the NfcTagService tests.

public sealed class FakeNfcTagRepository : INfcTagRepository
{
  public Dictionary<string, NfcTagPrincipal> Tags { get; } = [];
  public Dictionary<(Guid HabitId, DateOnly Date), HabitExecutionPrincipal> Executions { get; } = [];

  public Task<Result<NfcTagPrincipal?>> Get(string tagId)
  {
    Tags.TryGetValue(tagId, out var tag);
    return Task.FromResult<Result<NfcTagPrincipal?>>(tag);
  }

  public Task<Result<NfcTagPrincipal>> Upsert(string tagId, string userId, Guid habitId)
  {
    var claimedAt = Tags.TryGetValue(tagId, out var existing) ? existing.ClaimedAt : DateTime.UtcNow;
    var tag = new NfcTagPrincipal
    {
      Id = tagId,
      UserId = userId,
      ClaimedAt = claimedAt,
      Record = new NfcTagRecord { HabitId = habitId }
    };
    Tags[tagId] = tag;
    return Task.FromResult<Result<NfcTagPrincipal>>(tag);
  }

  public Task<Result<Unit?>> Delete(string tagId, string userId)
  {
    if (Tags.TryGetValue(tagId, out var tag) && tag.UserId == userId)
    {
      Tags.Remove(tagId);
      return Task.FromResult<Result<Unit?>>(new Unit());
    }
    return Task.FromResult<Result<Unit?>>((Unit?)null);
  }

  public Task<Result<HabitExecutionPrincipal?>> GetExecutionForDate(Guid habitId, DateOnly date)
  {
    Executions.TryGetValue((habitId, date), out var exec);
    return Task.FromResult<Result<HabitExecutionPrincipal?>>(exec);
  }
}

public sealed class FakeHabitService : IHabitService
{
  // (userId, habitId) → current version; unknown pairs resolve to null.
  public Dictionary<(string UserId, Guid HabitId), HabitVersionPrincipal> Versions { get; } = [];

  public Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId)
  {
    Versions.TryGetValue((userId, habitId), out var hv);
    return Task.FromResult<Result<HabitVersionPrincipal?>>(hv);
  }

  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch) =>
    throw new NotImplementedException();

  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord) =>
    throw new NotImplementedException();

  public Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord, bool enabled) =>
    throw new NotImplementedException();

  public Task<Result<Unit?>> Delete(Guid habitId, string userId) => throw new NotImplementedException();

  public Task<Result<int>> MarkDailyFailures(List<Guid> habitIds, DateOnly date) =>
    throw new NotImplementedException();

  public Task<Result<int>> MarkDailyFailuresForTimezonesNearMidnight(DateTime? nowUtc = null) =>
    throw new NotImplementedException();

  public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, string? notes) =>
    throw new NotImplementedException();

  public Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, string? notes) =>
    throw new NotImplementedException();

  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch) =>
    throw new NotImplementedException();
}
