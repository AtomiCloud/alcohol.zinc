using CSharp_Result;
using NodaMoney;

namespace Domain.Disbursement;

// Domain port over the donation vendor (Pledge). Keeps the service vendor-agnostic and
// fakeable; the App layer implements it against the Pledge HTTP client.
public interface IDonationGateway
{
  // Create a donation to a nonprofit. `reference` (our Disbursement.Id) is persisted on the
  // vendor record (Pledge `metadata`) so a crashed-before-mark attempt can be looked up and
  // reconciled instead of re-donated.
  Task<Result<DonationResult>> CreateDonation(DonationRequest req);

  // Look up a previously-created donation by our `reference`. Returns null when none exists
  // (the create never landed) so the caller can safely release the penalties for a fresh retry.
  Task<Result<DonationResult?>> FindDonationByReference(string reference);
}

public record DonationRequest
{
  public required string OrganizationId { get; init; }
  public required Money Amount { get; init; }
  public required string Reference { get; init; } // our Disbursement.Id; stored as Pledge metadata
  public required string DonorFirstName { get; init; }
  public required string DonorLastName { get; init; }
  public required string DonorEmail { get; init; }
}

public record DonationResult
{
  public required string DonationId { get; init; } // Pledge donation id
  public required string Status { get; init; }     // Pledge status, e.g. "processed"
}
