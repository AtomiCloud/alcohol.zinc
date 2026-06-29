using CSharp_Result;
using Domain.Disbursement;

namespace IntTest.Disbursement;

// Minimal IDonationGateway stub for the DB-backed round-trip test: records CreateDonation calls
// and returns a canned result, so the test exercises the real repository ledger without a vendor.
public sealed class StubDonationGateway(
  Func<DonationRequest, Result<DonationResult>> create,
  Func<string, Result<DonationResult?>>? find = null) : IDonationGateway
{
  public List<DonationRequest> CreateCalls { get; } = [];

  public static StubDonationGateway Succeeds(string donationId)
    => new(_ => new DonationResult { DonationId = donationId, Status = "processed" });

  public Task<Result<DonationResult>> CreateDonation(DonationRequest req)
  {
    CreateCalls.Add(req);
    return Task.FromResult(create(req));
  }

  public Task<Result<DonationResult?>> FindDonationByReference(string reference)
    => Task.FromResult(find?.Invoke(reference) ?? (DonationResult?)null);
}
