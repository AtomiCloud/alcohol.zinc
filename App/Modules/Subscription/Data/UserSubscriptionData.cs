using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Subscription.Data;

public class UserSubscriptionData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(32)]
  public required string Tier { get; set; }

  public required int Status { get; set; } // SubscriptionStatus as int

  public required DateTime PeriodStart { get; set; } // UTC

  public required DateTime PeriodEnd { get; set; } // UTC

  public required bool CancelAtPeriodEnd { get; set; }

  [MaxLength(32)]
  public string? NextTier { get; set; }

  [MaxLength(256)]
  public string? KonnectCustomerId { get; set; }

  // Mirror watermark: NULL or < UpdatedAt means the Konnect mirror is stale.
  public DateTime? KonnectSyncedAt { get; set; }

  // Intent id of an in-flight charge attempt only; cleared once the charge settles.
  [MaxLength(256)]
  public string? LastChargeIntentId { get; set; }

  // Idempotency key the stored intent was created under; an intent is only
  // reconciled by a charge using the same key (guards cross-tier/period reuse).
  [MaxLength(128)]
  public string? LastChargeKey { get; set; }

  // Renewal charge lease; while in the future, interactive upgrades are rejected.
  public DateTime? RenewingUntil { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Foreign Keys & Navigation
  [MaxLength(128)]
  public required string UserId { get; set; }

  public virtual UserData? User { get; set; }
}
