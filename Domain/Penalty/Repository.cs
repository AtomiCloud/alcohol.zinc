using CSharp_Result;

namespace Domain.Penalty;

public interface IPenaltyRepository
{
  // Idempotent upsert: returns true if newly inserted, false if HabitExecutionId already present.
  Task<Result<bool>> EnqueuePending(PenaltyRecord record);

  // Drain query: Status==Pending ordered by CreatedAt, .Take(batchSize), AsNoTracking.
  Task<Result<List<PenaltyPrincipal>>> GetPending(int batchSize);

  // Atomically set Status=Charged + PaymentIntentId, and credit charity_balance by this penalty's amount, in ONE transaction.
  Task<Result<Unit>> MarkCharged(Guid id, string paymentIntentId);

  // Keep Pending, persist intent id from a requires-action create, set Attempts.
  Task<Result<Unit>> MarkPending(Guid id, string paymentIntentId, int attempts);

  // Persist ONLY the PaymentIntentId from a freshly-created intent (no status/attempts
  // change), so a subsequent confirm failure doesn't lose it and the retry reconciles
  // this intent instead of creating a new one (which would reuse the request_id).
  Task<Result<Unit>> SetIntentId(Guid id, string paymentIntentId);

  // Attempts += 1, set LastError, keep Pending (transient retry).
  Task<Result<Unit>> Bump(Guid id, string error);

  // Status=Failed, set LastError.
  Task<Result<Unit>> MarkFailed(Guid id, string error);

  // Status=Skipped (no-consent terminal).
  Task<Result<Unit>> MarkSkipped(Guid id);

  Task<Result<List<PenaltyPrincipal>>> Search(PenaltySearch search);
}
