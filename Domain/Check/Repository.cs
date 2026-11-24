using CSharp_Result;
using Domain.Habit;

namespace Domain.Check;

public record NewCheck(Guid HabitId, DateOnly LocalDate, string Timezone, DateTime OpensAtUtc, DateTime DeadlineUtc);

// Result of a settle: the execution now recorded for the check's day. Adopted is true
// when another writer had already recorded that day, so nothing new was settled.
public record SettleOutcome(HabitExecutionPrincipal Execution, bool Adopted);

public interface ICheckRepository
{
  // Enabled, non-deleted habits with their current version and the user's timezone.
  // Filter by user and/or habit ids; both null means every enabled habit (scheduler only).
  Task<Result<List<SchedulableHabit>>> ListSchedulableHabits(string? userId, List<Guid>? habitIds);

  // Inserts the checks that don't exist yet (unique per habit and local date).
  Task<Result<int>> CreatePending(List<NewCheck> checks);

  // Pending checks whose day ended before deadlineBeforeUtc, or, for habits bound to one of
  // slowProofs' plugins, before that plugin's own cutoff. Oldest first.
  Task<Result<List<CheckPrincipal>>> ListDuePending(DateTime deadlineBeforeUtc,
    IReadOnlyDictionary<string, DateTime> slowProofs, int limit);
  Task<Result<List<CheckPrincipal>>> ListPending(List<Guid> habitIds);
  Task<Result<List<CheckPrincipal>>> ListForDate(List<Guid> habitIds, DateOnly localDate);
  Task<Result<List<CheckPrincipal>>> ListCovering(Guid habitId, DateTime atUtc);
  Task<Result<CheckPrincipal?>> Get(Guid id);
  Task<Result<CheckPrincipal?>> Get(Guid habitId, DateOnly localDate);

  Task<Result<bool>> UpdatePendingWindow(Guid id, string timezone, DateTime opensAtUtc, DateTime deadlineUtc);
  Task<Result<int>> DeletePending(List<Guid> ids);

  // Ids of the given checks whose habit already has an execution on the check's date
  // (written by an older release or a legacy path); settling them only adopts it.
  Task<Result<HashSet<Guid>>> ListRecorded(List<Guid> checkIds);

  Task<Result<bool>> EvidenceExists(Guid habitId, string proofKey, string externalId);

  // Settles a Pending check in one transaction: records the execution, links it, records
  // the evidence and writes a CheckSettled outbox event. Null when the check was no
  // longer Pending or the evidence was already recorded.
  Task<Result<SettleOutcome?>> Settle(SettleCheck command);

  // Undoes a Done that the given evidence settled: deletes that evidence, the execution and the
  // settle's outbox events, and makes the check Pending again. False when the check is no
  // longer Done by that evidence.
  Task<Result<bool>> Reopen(Guid checkId, string userId, string proofKey, string externalId);

  // ExternalId of the evidence each given check was settled by, for the plugin's evidence.
  Task<Result<Dictionary<Guid, string>>> ListEvidence(List<Guid> checkIds, string proofKey);

  // Outbox
  Task<Result<List<CheckEventPrincipal>>> ClaimEvents(int batchSize, int maxAttempts, DateTime nowUtc);
  Task<Result<bool>> HasDelivery(Guid eventId, string bindingId);
  Task<Result<bool>> RecordDelivery(Guid eventId, string bindingId);
  Task<Result<Unit>> MarkDispatched(Guid eventId, DateTime nowUtc);
  Task<Result<Unit>> MarkFailed(Guid eventId, string error, DateTime nextAttemptAtUtc);
  Task<Result<FailedExecutionRow?>> GetStakeForExecution(Guid executionId);
}
