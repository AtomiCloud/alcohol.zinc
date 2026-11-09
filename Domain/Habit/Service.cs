using CSharp_Result;
using Domain.Check;
using Domain.Entitlement;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Domain.Habit;

public class HabitService(
  IHabitRepository repo,
  IEntitlementService entitlementService,
  ICheckService checkService,
  ILogger<HabitService> logger
) : IHabitService
{
  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
  {
    return repo.SearchHabits(habitSearch);
  }

  public Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId)
    => repo.GetCurrentVersion(userId, habitId);

  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord, Guid? routineId)
  {
    return repo.Create(userId, versionRecord, routineId);
  }

  public async Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId,
    HabitVersionRecord versionRecord, bool enabled)
  {
    // First verify the habit belongs to the user
    var habitResult = await repo.GetHabit(habitId);
    HabitPrincipal? habit = habitResult;

    if (habit == null || habit.UserId != userId)
    {
      return (HabitVersionPrincipal?)null;
    }

    if (enabled && !habit.Record.Enabled)
    {
      var allowed = await entitlementService.EnsureHabitsAllowed(userId);
      if (allowed.IsFailure()) return allowed.FailureOrDefault();
    }

    var updated = await repo.Update(habitId, userId, versionRecord, enabled);
    if (updated.IsSuccess() && updated.Get() != null)
    {
      // Days, deadline or enabled may have changed: move this habit's pending checks.
      var retimed = await checkService.RetimePending(userId, [habitId]);
      if (retimed.IsFailure()) logger.LogError(retimed.FailureOrDefault(), "Retiming checks failed for HabitId={HabitId}", habitId);
    }
    return updated;
  }

  public Task<Result<Unit?>> Delete(Guid habitId, string userId)
  {
    return repo.Delete(habitId, userId);
  }

  public async Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, string? notes)
  {
    var hv = (HabitVersionPrincipal?)await repo.GetVersion(habitVersionId);
    if (hv == null) return NotCompletable(habitVersionId);

    // The app, widget and NFC tap complete through Report as manual evidence.
    var reported = await checkService.Report(userId, hv.HabitId, new Evidence
    {
      ProofKey = CheckService.ManualProof,
      ExternalId = Guid.NewGuid().ToString(),
      Verdict = EvidenceVerdict.Passed,
      OccurredAtUtc = DateTime.UtcNow,
      Notes = notes
    });
    if (reported.IsFailure()) return reported.FailureOrDefault();
    var execution = reported.Get();
    if (execution == null) return NotCompletable(habitVersionId);
    return execution;
  }

  public async Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, string? notes)
  {
    var hv = (HabitVersionPrincipal?)await repo.GetVersion(habitVersionId);
    if (hv == null) return NotCompletable(habitVersionId);

    var skipped = await checkService.Skip(userId, hv.HabitId, notes);
    if (skipped.IsFailure()) return skipped.FailureOrDefault();
    var execution = skipped.Get();
    if (execution == null) return NotCompletable(habitVersionId);
    return execution;
  }

  // Same failure the endpoints returned before checks existed (404).
  private static NotFoundException NotCompletable(Guid habitVersionId) =>
    new("Habit not found, not enabled, not scheduled for this day, or already has execution",
      typeof(HabitExecutionPrincipal), habitVersionId.ToString());

  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId,
    HabitExecutionSearch habitExecutionSearch)
  {
    return repo.SearchHabitExecutions(userId, habitExecutionSearch);
  }
}
