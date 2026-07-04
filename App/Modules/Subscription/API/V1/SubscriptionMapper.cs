using Domain.Subscription;

namespace App.Modules.Subscription.API.V1;

public static class SubscriptionMapper
{
  // No subscription row (or one that grants nothing) presents as the free tier.
  public static SubscriptionRes ToFreeRes(string userId)
  {
    return new SubscriptionRes(
      userId,
      SubscriptionService.FreeTier,
      "none",
      null,
      null,
      false,
      null
    );
  }

  public static SubscriptionRes ToRes(this UserSubscriptionPrincipal p)
  {
    var rec = p.Record;
    if (rec.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.PendingActivation)
      return ToFreeRes(rec.UserId);

    return new SubscriptionRes(
      rec.UserId,
      rec.Tier,
      rec.Status.ToString().ToLowerInvariant(),
      rec.PeriodStart.ToString("O"),
      rec.PeriodEnd.ToString("O"),
      rec.CancelAtPeriodEnd,
      rec.NextTier
    );
  }
}
