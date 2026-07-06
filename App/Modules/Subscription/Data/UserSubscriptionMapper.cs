using Domain.Subscription;

namespace App.Modules.Subscription.Data;

public static class UserSubscriptionMapper
{
  public static UserSubscriptionPrincipal ToPrincipal(this UserSubscriptionData data)
  {
    return new UserSubscriptionPrincipal
    {
      Id = data.Id,
      Record = new UserSubscriptionRecord
      {
        UserId = data.UserId,
        Tier = data.Tier,
        Status = (SubscriptionStatus)data.Status,
        PeriodStart = data.PeriodStart,
        PeriodEnd = data.PeriodEnd,
        CancelAtPeriodEnd = data.CancelAtPeriodEnd,
        NextTier = data.NextTier,
        LastChargeIntentId = data.LastChargeIntentId,
        LastChargeKey = data.LastChargeKey,
        RenewingUntil = data.RenewingUntil
      },
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };
  }

  public static UserSubscriptionData ToData(this UserSubscriptionRecord record)
  {
    var now = DateTime.UtcNow;
    return new UserSubscriptionData
    {
      Id = Guid.NewGuid(),
      UserId = record.UserId,
      Tier = record.Tier,
      Status = (int)record.Status,
      PeriodStart = record.PeriodStart,
      PeriodEnd = record.PeriodEnd,
      CancelAtPeriodEnd = record.CancelAtPeriodEnd,
      NextTier = record.NextTier,
      LastChargeIntentId = record.LastChargeIntentId,
      LastChargeKey = record.LastChargeKey,
      RenewingUntil = record.RenewingUntil,
      CreatedAt = now,
      UpdatedAt = now
    };
  }
}
