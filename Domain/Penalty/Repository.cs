using CSharp_Result;

namespace Domain.Penalty;

public interface IPenaltyRepository
{
  // Idempotent upsert: returns true if newly inserted, false if HabitExecutionId already present.
  // Inserted as Pending, or as Held when its charity is decommissioned (decided under a lock on the
  // charity row, so it serializes with a decommission).
  Task<Result<bool>> EnqueuePending(PenaltyRecord record);

  // Drain query: Status==Pending ordered by CreatedAt, .Take(batchSize), AsNoTracking.
  Task<Result<List<PenaltyPrincipal>>> GetPending(int batchSize);

  // Atomically set Status=Charged + PaymentIntentId, and credit charity_balance by this penalty's amount, in ONE transaction.
  // True if this call charged it; false if it was already Charged or is gone (so tell nobody again).
  Task<Result<bool>> MarkCharged(Guid id, string paymentIntentId);

  // Read fresh; throws PenaltyHeldException if the penalty is Held. Called right before a confirm.
  Task<Result<Unit>> EnsureNotHeld(Guid id);

  // Keep Pending, persist intent id from a requires-action create, set Attempts.
  Task<Result<Unit>> MarkPending(Guid id, string paymentIntentId, int attempts);

  // Persist ONLY the PaymentIntentId from a freshly-created intent (no status/attempts
  // change), so a subsequent confirm failure doesn't lose it and the retry reconciles
  // this intent instead of creating a new one (which would reuse the request_id).
  Task<Result<Unit>> SetIntentId(Guid id, string paymentIntentId);

  // Attempts += 1, set LastError, keep Pending (transient retry).
  Task<Result<Unit>> Bump(Guid id, string error);

  // Status=Failed, set LastError. True if the row was marked; false if it was already final (Charged, or
  // Held by a charity decommission) or gone, so the caller must not tell the user the payment failed.
  Task<Result<bool>> MarkFailed(Guid id, string error);

  // Held rows (charity decommissioned) that already have a payment intent: an earlier attempt may have
  // been confirmed at the provider before the hold. Claims a batch for a LOOK-UP, never a confirm:
  // Attempts < maxChecks, last touched before `olderThan` ago. Claiming stamps UpdatedAt (rows stay
  // Held), so overlapping passes never look up the same row within `olderThan`.
  Task<Result<List<PenaltyPrincipal>>> GetHeldToReconcile(int batchSize, int maxChecks, TimeSpan olderThan);

  // Record one look-up of a Held row: Attempts += 1, LastError = note. Stays Held.
  Task<Result<Unit>> NoteHeldCheck(Guid id, string note);

  // Status=Skipped (no-consent terminal).
  Task<Result<Unit>> MarkSkipped(Guid id);

  Task<Result<List<PenaltyPrincipal>>> Search(PenaltySearch search);
}
