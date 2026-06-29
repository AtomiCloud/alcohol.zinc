using CSharp_Result;

namespace App.Modules.Charities.Sync;

public interface IPledgeClient
{
  Task<Result<IEnumerable<PledgeCauseDto>>> GetCauses(CancellationToken ct = default);
  Task<Result<PledgeOrganizationsPage>> GetOrganizations(int page, int perPage, string? causeKey = null, string[]? countries = null, DateTimeOffset? updatedSince = null, CancellationToken ct = default);

  // POST /v1/donations — record a donation to a nonprofit (Pledge batches & bills our payment
  // method on file). A 4xx/5xx is surfaced as a failed Result (not thrown) so the caller can
  // record it and retry rather than aborting the batch.
  Task<Result<PledgeDonationDto>> CreateDonation(PledgeDonationReq req, CancellationToken ct = default);

  // GET /v1/donations — one page of donations made through our account, newest first. Used by
  // reconciliation to find a donation by the metadata we stamped on it.
  Task<Result<PledgeDonationsPage>> GetDonations(int page, CancellationToken ct = default);
}
