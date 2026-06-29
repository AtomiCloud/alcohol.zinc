using System.Globalization;
using App.Modules.Charities.Sync;
using CSharp_Result;
using Domain.Disbursement;

namespace App.Modules.Disbursement;

// App-side implementation of the domain donation port over the Pledge HTTP client. Maps the
// vendor-agnostic DonationRequest to Pledge's body (amount as a decimal string, our reference
// stored in `metadata`) and reconciles by scanning donations for that metadata.
public class PledgeDonationGateway(IPledgeClient pledge, ILogger<PledgeDonationGateway> logger) : IDonationGateway
{
  // How many donation pages to scan when reconciling. The daily batch volume is small, so the
  // donation we are looking for (if it landed) sits within the first few pages (newest first).
  private const int MaxReconcilePages = 20;

  public async Task<Result<DonationResult>> CreateDonation(DonationRequest req)
  {
    var body = new PledgeDonationReq
    {
      Email = req.DonorEmail,
      FirstName = req.DonorFirstName,
      LastName = req.DonorLastName,
      // Pledge expects a decimal string in major units (e.g. "5.00"); there is no currency field.
      Amount = req.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture),
      OrganizationId = req.OrganizationId,
      Metadata = req.Reference // our Disbursement.Id — the correlation key for reconciliation
    };

    return await pledge.CreateDonation(body)
      .Then(dto => new DonationResult { DonationId = dto.Id, Status = dto.Status }, Errors.MapNone);
  }

  // Returns the matching donation if found; null ONLY when the scan conclusively reached the end
  // without a match; and a FAILURE when the lookup is inconclusive (a page fetch failed, or we hit
  // the page cap with more pages still unscanned). The distinction matters: the caller releases the
  // penalties on a conclusive null but must keep the disbursement Pending on a failure, so an
  // inconclusive lookup never causes a re-donation.
  public async Task<Result<DonationResult?>> FindDonationByReference(string reference)
  {
    for (var page = 1; page <= MaxReconcilePages; page++)
    {
      var pageRes = await pledge.GetDonations(page);
      if (pageRes.IsFailure()) return pageRes.FailureOrDefault()!;

      var data = pageRes.SuccessOrDefault();
      if (data == null || data.Results.Length == 0)
        return (DonationResult?)null; // reached the end with no match

      var match = data.Results.FirstOrDefault(d => string.Equals(d.Metadata, reference, StringComparison.Ordinal));
      if (match != null)
      {
        logger.LogInformation("Reconcile matched donation {DonationId} for reference {Reference}", match.Id, reference);
        return new DonationResult { DonationId = match.Id, Status = match.Status };
      }

      if (string.IsNullOrEmpty(data.NextUri))
        return (DonationResult?)null; // last page, conclusively not found
    }

    // Hit the page cap with more pages remaining: we did NOT scan to the end, so we cannot say the
    // donation is absent. Inconclusive -> failure, so the caller leaves the disbursement Pending.
    logger.LogWarning("Reconcile for reference {Reference} hit the {Cap}-page cap without reaching the end; inconclusive", reference, MaxReconcilePages);
    return new InvalidOperationException(
      $"Reconcile lookup for {reference} exceeded {MaxReconcilePages} pages without conclusion");
  }
}
