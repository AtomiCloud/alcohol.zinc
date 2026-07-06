using CSharp_Result;

namespace Domain.Subscription;

public interface ISubscriptionRepository
{
  Task<Result<UserSubscriptionPrincipal?>> GetByUserId(string userId);

  // One row per user (unique UserId). Reuses an existing row (e.g. a stale
  // PendingActivation or Cancelled one) rather than inserting a duplicate.
  Task<Result<UserSubscriptionPrincipal>> Upsert(UserSubscriptionRecord record);

  // Persist ONLY the intent id + the idempotency key it was created under
  // (before confirm), so a confirm failure doesn't lose it and a retry of the
  // SAME logical charge reconciles this intent instead of creating a new one.
  Task<Result<Unit>> SetIntentId(Guid id, string intentId, string chargeKey);

  // First-time activation / upgrade: Status=Active, tier + period set,
  // CancelAtPeriodEnd and NextTier reset, LastChargeIntentId/Key cleared
  // (charge settled). Guarded: rejected (returns false) while a live renewal
  // lease is held, so an interactive activation can never reset the period
  // underneath a renewal charge in flight — the caller surfaces busy and the
  // client's retry reconciles the already-recorded intent without recharging.
  Task<Result<bool>> Activate(Guid id, string tier, DateTime periodStart, DateTime periodEnd, DateTime nowUtc);

  // Successful renewal: Status=Active, applies the (possibly downgraded) tier,
  // rolls the period, CLEARS LastChargeIntentId/Key + NextTier. Guarded: only
  // applies when the row's PeriodEnd still equals `periodStart` (the old period
  // end), so a renewal computed from a stale snapshot cannot clobber a period
  // an interactive upgrade just reset. Returns false when the guard rejected.
  Task<Result<bool>> RollPeriod(Guid id, string tier, DateTime periodStart, DateTime periodEnd);

  Task<Result<Unit>> SetCancelAtPeriodEnd(Guid id, bool value);

  Task<Result<Unit>> SetNextTier(Guid id, string? nextTier);

  Task<Result<Unit>> MarkGrace(Guid id);

  Task<Result<Unit>> MarkCancelled(Guid id);

  // Atomically claim the row for a renewal charge: succeeds only when the
  // period is still the expected one AND no live lease is held. While claimed,
  // Subscribe rejects upgrades on this row, serializing the two charge paths.
  Task<Result<bool>> TryClaimRenewal(Guid id, DateTime expectedPeriodEnd, DateTime until, DateTime nowUtc);

  // Releases only the caller's own lease (matched on `until`), so a pass that
  // overran its lease cannot null out a lease a newer claimant now owns.
  Task<Result<Unit>> ReleaseRenewal(Guid id, DateTime until);

  // Renewal scan: Status in (Active, Grace) AND PeriodEnd <= nowUtc.
  Task<Result<List<UserSubscriptionPrincipal>>> GetDue(DateTime nowUtc, int batchSize);


}
