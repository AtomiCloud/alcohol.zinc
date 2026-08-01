namespace Domain.Subscription;

// One immutable row per subscription lifecycle event. The UserSubscriptions row
// stays the single source of CURRENT truth; this table is the history — it
// powers receipts/billing history, audit ("did we ever schedule this
// downgrade?"), and gives failed side-effects (e.g. a pause reconcile after a
// lapse) a durable record to retry from. Rows are never updated or deleted.
public enum SubscriptionEventType
{
  // First activation, or re-activation after Cancelled. Money event.
  Activated,
  // Immediate tier switch on an active row (prorated, possibly $0). Money event.
  Upgraded,
  // NextTier set; applies at the period roll.
  DowngradeScheduled,
  // NextTier cleared before it applied.
  DowngradeUndone,
  // CancelAtPeriodEnd set; access retained until PeriodEnd.
  CancelScheduled,
  // CancelAtPeriodEnd cleared before the period ended.
  Resumed,
  // Period rolled after a successful renewal charge (a scheduled downgrade
  // lands here — Tier is the tier just applied). Money event.
  Renewed,
  // A charge attempt did not settle (interactive subscribe or renewal retry).
  ChargeFailed,
  // Renewal failed and the row entered the grace window (fires once per lapse).
  GraceEntered,
  // Grace window exhausted; effective access dropped to free.
  Lapsed,
  // A user-requested cancel took effect at the period roll.
  Cancelled
}

public record SubscriptionEventRecord
{
  public required string UserId { get; init; }
  public required SubscriptionEventType EventType { get; init; }
  public required DateTime OccurredAt { get; init; } // UTC
  // Tier snapshot at the moment of the event (the tier the event concerns).
  public required string Tier { get; init; }
  public string? NextTier { get; init; }
  // Money events only.
  public int? AmountCents { get; init; }
  public string? Currency { get; init; }
  public string? ChargeIntentId { get; init; }
  // The period end this event relates to (renewal anchor / access-until).
  public DateTime? PeriodEnd { get; init; }
  // Small human-readable context, e.g. the gateway status of a failed charge.
  public string? Detail { get; init; }
}

public record SubscriptionEventPrincipal
{
  public required Guid Id { get; init; }
  public required SubscriptionEventRecord Record { get; init; }
}
