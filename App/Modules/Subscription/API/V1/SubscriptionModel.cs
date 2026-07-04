namespace App.Modules.Subscription.API.V1;

public record SubscribeReq(
  string Tier
);

public record ChangeTierReq(
  string Tier
);

public record SubscriptionCtaRes(
  string Variant,
  string Tier
);

public record SubscriptionRes(
  string UserId,
  string Tier,
  string Status,
  string? PeriodStart,
  string? PeriodEnd,
  bool CancelAtPeriodEnd,
  string? NextTier
);
