using Domain.Subscription;

namespace App.Modules.Subscription.Data
{
    public static class SubscriptionEventMapper
    {
        public static SubscriptionEventData ToData(this SubscriptionEventRecord record)
        {
            return new SubscriptionEventData
            {
                UserId = record.UserId,
                EventType = (int)record.EventType,
                OccurredAt = record.OccurredAt,
                Tier = record.Tier,
                NextTier = record.NextTier,
                AmountCents = record.AmountCents,
                Currency = record.Currency,
                ChargeIntentId = record.ChargeIntentId,
                PeriodEnd = record.PeriodEnd,
                Detail = record.Detail
            };
        }

        public static SubscriptionEventRecord ToRecord(this SubscriptionEventData data)
        {
            return new SubscriptionEventRecord
            {
                UserId = data.UserId,
                EventType = (SubscriptionEventType)data.EventType,
                OccurredAt = data.OccurredAt,
                Tier = data.Tier,
                NextTier = data.NextTier,
                AmountCents = data.AmountCents,
                Currency = data.Currency,
                ChargeIntentId = data.ChargeIntentId,
                PeriodEnd = data.PeriodEnd,
                Detail = data.Detail
            };
        }

        public static SubscriptionEventPrincipal ToPrincipal(this SubscriptionEventData data)
        {
            return new SubscriptionEventPrincipal
            {
                Id = data.Id,
                Record = data.ToRecord()
            };
        }
    }
}
