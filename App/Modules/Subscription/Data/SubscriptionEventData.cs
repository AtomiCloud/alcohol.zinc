using System.ComponentModel.DataAnnotations;

namespace App.Modules.Subscription.Data
{
    // Append-only subscription lifecycle history. Rows are never updated or
    // deleted — no UpdatedAt, no soft delete. See Domain/Subscription/EventModel.cs.
    public class SubscriptionEventData
    {
        [Key]
        public Guid Id { get; set; }
        [MaxLength(128)]
        public required string UserId { get; set; }
        public required int EventType { get; set; }
        public required DateTime OccurredAt { get; set; } // UTC
        [MaxLength(64)]
        public required string Tier { get; set; }
        [MaxLength(64)]
        public string? NextTier { get; set; }
        public int? AmountCents { get; set; }
        [MaxLength(8)]
        public string? Currency { get; set; }
        [MaxLength(256)]
        public string? ChargeIntentId { get; set; }
        public DateTime? PeriodEnd { get; set; }
        [MaxLength(512)]
        public string? Detail { get; set; }
    }
}
