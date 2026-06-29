using CSharp_Result;

namespace Domain.Disbursement;

public interface IDisbursementRepository
{
  // Atomically CLAIM the pending payout: in ONE row-locked transaction, group the charged-but-
  // unpaid penalties by (charity, currency), and for each group whose total >= minPayoutCents
  // and whose charity has a Pledge org id, create a Pending Disbursement and stamp those
  // penalties' DisbursementId. Stamping at claim time is what stops a concurrent or retried pass
  // from re-selecting the same penalties and double-donating (mirrors the Pending->Processing
  // claim in the penalty drain). Charities with no Pledge org id are left unclaimed.
  Task<Result<List<ClaimedPayout>>> ClaimPendingPayouts(long minPayoutCents, int maxGroups);

  // Idempotent terminal success: set Status=Completed + ProviderDonationId. Row-locked; a
  // repeat (e.g. a reconcile racing the original) is a no-op.
  Task<Result<Unit>> MarkDisbursed(Guid disbursementId, string providerDonationId);

  // Terminal failure: set Status=Failed, record LastError, bump Attempts, and RELEASE the
  // claimed penalties (DisbursementId -> NULL) so a later pass re-claims them into a fresh
  // disbursement. Row-locked + idempotent.
  Task<Result<Unit>> MarkFailed(Guid disbursementId, string error);

  // Pending disbursements older than `olderThan` — a process that created the donation but
  // crashed before MarkDisbursed leaves one of these. Reconciled against the vendor.
  Task<Result<List<DisbursementPrincipal>>> GetReconcilable(TimeSpan olderThan, int batchSize);
}
