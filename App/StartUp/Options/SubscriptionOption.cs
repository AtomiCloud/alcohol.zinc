using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

// Subscription tier catalog + renewal worker settings — the single source of
// truth for tiers, caps and prices. Keep in sync with the public pricing page
// (alcohol.argon src/lib/billing/pricing.ts + Pricing.tsx).
public class SubscriptionOption
{
  public const string Key = "Subscription";

  // Master switch for the background renewal worker. Off by default so renewals
  // only run where deliberately enabled per landscape.
  public bool RenewalEnabled { get; set; } = false;

  // Days after PeriodEnd during which a failed renewal keeps paid entitlements
  // and is retried daily, before the subscription is cancelled to free.
  [Range(1, 90)] public int GracePeriodDays { get; set; } = 7;

  [Required] public Dictionary<string, SubscriptionTierOption> Tiers { get; set; } = [];
}

public class SubscriptionTierOption
{
  [Range(0, long.MaxValue)] public long PriceCents { get; set; }

  [Required] public string Currency { get; set; } = "USD";

  [Range(0, int.MaxValue)] public int HabitsMax { get; set; }

  [Range(0, int.MaxValue)] public int SkipsMonthly { get; set; }

  [Range(0, int.MaxValue)] public int VacationWindowsYearly { get; set; }

  [Range(0, int.MaxValue)] public int FreezeBase { get; set; }
}
