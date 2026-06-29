using NodaMoney;

namespace Domain.Disbursement;

public enum DisbursementStatus
{
  Pending,   // claimed (penalties stamped), donation not yet confirmed at Pledge
  Completed, // Pledge donation created + reconciled; this is the only "paid out" state
  Failed     // donation attempt failed; penalties were released back to pending-payout
}

// Business data for one payout to a single (charity, currency).
public record DisbursementRecord
{
  public required Guid CharityId { get; init; }
  public required Money Amount { get; init; } // currency carried by Money; cents at the data boundary
  public required string PledgeOrganizationId { get; init; }
  public required DisbursementStatus Status { get; init; }
  public string? ProviderDonationId { get; init; } // Pledge donation id, set on Completed
  public required int Attempts { get; init; }
  public string? LastError { get; init; }
}

public record DisbursementPrincipal
{
  public required Guid Id { get; init; }
  public required DisbursementRecord Record { get; init; }
  public required DateTime CreatedAt { get; init; }
  public required DateTime UpdatedAt { get; init; }
}

// Legal donor of record recorded on every donation (LazyTax). Sourced from config.
public record DonorIdentity
{
  public required string FirstName { get; init; }
  public required string LastName { get; init; }
  public required string Email { get; init; }
}

// A claimed payout group: the Disbursement row plus the charity's Pledge org id and the
// amount to donate. Returned by ClaimPendingPayouts so the worker can issue the donation
// without re-resolving anything. The penalties are already stamped with DisbursementId.
public record ClaimedPayout
{
  public required Guid DisbursementId { get; init; }
  public required Guid CharityId { get; init; }
  public required string PledgeOrganizationId { get; init; }
  public required Money Amount { get; init; }
}
