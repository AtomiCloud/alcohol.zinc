using NodaMoney;

namespace Domain.Subscription;

public enum SubscriptionStatus
{
  // Row created for a first-time subscribe whose charge has not succeeded yet.
  // Grants no paid tier; a retried Subscribe reconciles the same charge attempt.
  PendingActivation,
  Active,
  // Renewal charge failed; paid entitlements are retained until the grace window
  // (PeriodEnd + plan.GracePeriodDays) lapses, after which the row is Cancelled.
  Grace,
  Cancelled
}

public record UserSubscriptionRecord
{
  public required string UserId { get; init; }
  public required string Tier { get; init; }
  public required SubscriptionStatus Status { get; init; }
  public required DateTime PeriodStart { get; init; } // UTC
  public required DateTime PeriodEnd { get; init; }   // UTC
  public required bool CancelAtPeriodEnd { get; init; }

  // Pending downgrade target, applied when the period rolls (never mid-period).
  public string? NextTier { get; init; }

  public string? KonnectCustomerId { get; init; }

  // Mirror watermark: null or older than UpdatedAt means the Konnect mirror is
  // stale and the daily worker should re-push this row.
  public DateTime? KonnectSyncedAt { get; init; }

  // Airwallex intent id of an IN-FLIGHT (not yet succeeded) charge attempt only.
  // Cleared on Activate/RollPeriod so a settled period's intent can never be
  // reconciled as payment for a later charge (which would renew for free).
  public string? LastChargeIntentId { get; init; }

  // The idempotency key the stored intent was created under. An intent is only
  // reconciled by a charge using the SAME key — a leftover intent from a
  // different tier/period/attempt (different key ⇒ different price or charge)
  // must never be able to pay for the current one.
  public string? LastChargeKey { get; init; }

  // Renewal lease: while set (and in the future), the renewal worker owns this
  // row's charge; interactive upgrades are rejected so a successful renewal
  // charge can never be stranded by a concurrent Activate resetting the period.
  public DateTime? RenewingUntil { get; init; }
}

public record UserSubscriptionPrincipal
{
  public required Guid Id { get; init; }
  public required UserSubscriptionRecord Record { get; init; }
  public required DateTime CreatedAt { get; init; }
  public required DateTime UpdatedAt { get; init; }
}

// Resolved caps + price for one tier, sourced from local config (Konnect is a
// mirror, not the enforcement source).
public record SubscriptionPlan
{
  public required string Tier { get; init; }
  public required Money Price { get; init; }
  public required IReadOnlyDictionary<string, int> Caps { get; init; } // EntitlementKey -> cap
  public required int GracePeriodDays { get; init; }
}
