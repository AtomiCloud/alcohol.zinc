using CSharp_Result;

namespace Domain.Disbursement;

public interface IDisbursementService
{
  // Run one payout pass: reconcile any crashed-mid-donation disbursements, then claim and
  // donate the pending payouts. `donor` is the legal donor of record (LazyTax) recorded on
  // every Pledge donation. Returns the count of disbursements that reached Completed.
  Task<Result<int>> ProcessPending(DonorIdentity donor, long minPayoutCents, int maxGroups);
}
