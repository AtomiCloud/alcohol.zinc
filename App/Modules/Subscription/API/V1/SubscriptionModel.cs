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

// One tier of the public plan catalog. habitsMax is null when unlimited.
public record PlanRes(
  string Key,
  long PriceCents,
  string Currency,
  int? HabitsMax,
  bool HabitsUnlimited,
  int SkipsMonthly,
  int VacationWindowsYearly,
  int FreezeBase
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
