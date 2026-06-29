using CSharp_Result;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Domain.Disbursement;

public class DisbursementService(
  IDisbursementRepository repo,
  IDonationGateway gateway,
  ILogger<DisbursementService> logger
) : IDisbursementService
{
  // A Pending disbursement still in flight after this long is assumed to belong to a
  // crashed/hung pass and is eligible for reconciliation against the vendor.
  private static readonly TimeSpan ReconcileLease = TimeSpan.FromMinutes(10);

  public async Task<Result<int>> ProcessPending(DonorIdentity donor, long minPayoutCents, int maxGroups)
  {
    // 1) Reconcile first: settle (or release) any disbursement a previous pass created at the
    //    vendor but crashed before recording. These rows already have their penalties stamped,
    //    so the claim below will not re-pick them; reconciling here makes them terminal.
    var reconciled = await this.ReconcileInflight(maxGroups);
    if (reconciled.IsFailure())
      logger.LogError(reconciled.FailureOrDefault(), "Disbursement reconcile pass failed; continuing to claim");

    // 2) Claim and donate the pending payouts.
    return await repo.ClaimPendingPayouts(minPayoutCents, maxGroups)
      .ThenAwait(async claimed =>
      {
        var results = new List<Result<int>>();
        foreach (var c in claimed)
        {
          try
          {
            results.Add(await this.DonateOne(c, donor));
          }
          catch (OperationCanceledException)
          {
            // Host shutdown must abort the pass, not be swallowed into a Failed.
            throw;
          }
          catch (Exception ex)
          {
            // Per-(charity,currency) isolation: one group's unexpected throw must not abort the
            // batch and strand the rest. Record it against this disbursement (which releases its
            // penalties for retry) and keep going.
            logger.LogError(ex, "DonateOne threw for disbursement {Id}; isolating and continuing", c.DisbursementId);
            try
            {
              await repo.MarkFailed(c.DisbursementId, $"unhandled: {ex.Message}");
            }
            catch (Exception inner)
            {
              logger.LogError(inner, "Failed to record isolation for disbursement {Id}", c.DisbursementId);
            }
            results.Add(0);
          }
        }

        return results.ToResultOfSeq().Then(xs => xs.Sum(), Errors.MapAll);
      });
  }

  private async Task<Result<int>> DonateOne(ClaimedPayout c, DonorIdentity donor)
  {
    var req = new DonationRequest
    {
      OrganizationId = c.PledgeOrganizationId,
      Amount = c.Amount,
      Reference = c.DisbursementId.ToString(),
      DonorFirstName = donor.FirstName,
      DonorLastName = donor.LastName,
      DonorEmail = donor.Email
    };

    var donation = await gateway.CreateDonation(req);
    if (donation.IsSuccess())
    {
      var d = donation.Get();
      return await repo.MarkDisbursed(c.DisbursementId, d.DonationId).Then(_ => 1, Errors.MapAll);
    }

    // Donation failed: release the penalties so a later pass retries them in a fresh disbursement.
    var ex = donation.FailureOrDefault();
    return await repo.MarkFailed(c.DisbursementId, ex?.Message ?? "donation error").Then(_ => 0, Errors.MapAll);
  }

  // For each in-flight (Pending) disbursement past the lease, ask the vendor whether the donation
  // actually landed (matched by our reference == Disbursement.Id, stored in metadata). If it did,
  // mark Completed WITHOUT re-donating; if it never landed, release the penalties for a clean retry.
  private async Task<Result<int>> ReconcileInflight(int batchSize)
  {
    return await repo.GetReconcilable(ReconcileLease, batchSize)
      .ThenAwait(async stale =>
      {
        var results = new List<Result<int>>();
        foreach (var d in stale)
        {
          try
          {
            var found = await gateway.FindDonationByReference(d.Id.ToString());
            if (found.IsFailure())
            {
              // Unknown vendor state: leave the row Pending so the next pass retries the lookup
              // rather than risk releasing (and double-donating) a donation that did land.
              logger.LogWarning(found.FailureOrDefault(), "Reconcile lookup failed for disbursement {Id}; leaving Pending", d.Id);
              results.Add(0);
              continue;
            }

            var donation = found.SuccessOrDefault();
            results.Add(donation != null
              ? await repo.MarkDisbursed(d.Id, donation.DonationId).Then(_ => 1, Errors.MapAll)
              : await repo.MarkFailed(d.Id, "reconcile: no donation found at provider; released for retry").Then(_ => 0, Errors.MapAll));
          }
          catch (OperationCanceledException)
          {
            throw;
          }
          catch (Exception ex)
          {
            logger.LogError(ex, "Reconcile threw for disbursement {Id}; leaving Pending", d.Id);
            results.Add(0);
          }
        }

        return results.ToResultOfSeq().Then(xs => xs.Sum(), Errors.MapAll);
      });
  }
}
