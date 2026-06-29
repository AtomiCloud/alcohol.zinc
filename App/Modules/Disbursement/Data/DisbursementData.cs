using System.ComponentModel.DataAnnotations;
using App.Modules.Charities.Data;

namespace App.Modules.Disbursement.Data;

// One row per payout attempt to a single (charity, currency). The Id doubles as the
// idempotency key sent to Pledge so a crash-and-retry reconciles the same donation
// instead of minting a second one (mirrors the penalty PaymentIntentId reconcile).
public class DisbursementData
{
  [Key]
  public Guid Id { get; set; }
  public required Guid CharityId { get; set; }
  [MaxLength(8)]
  public required string Currency { get; set; }
  public long AmountCents { get; set; } // long: a payout sums many penalties
  public required int Status { get; set; } // store DisbursementStatus as int
  // PledgeOrganizationId is captured at claim time so a reconcile pass can re-issue the
  // donation with the SAME idempotency key without re-resolving the charity's external id.
  [MaxLength(256)]
  public required string PledgeOrganizationId { get; set; }
  [MaxLength(256)]
  public string? ProviderDonationId { get; set; } // Pledge donation id, set on success
  public int Attempts { get; set; } = 0;
  [MaxLength(2048)]
  public string? LastError { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  public virtual CharityData? Charity { get; set; }
}
