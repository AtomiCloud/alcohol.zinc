using CSharp_Result;

namespace Domain.Subscription;

// Domain port over the plan catalog. The App layer implements this over
// SubscriptionOption config so the Domain stays config-agnostic (same layering
// as IFreezePolicy). Konnect mirrors this catalog; it is not the source.
public interface ISubscriptionPlanProvider
{
  // Fails with InvalidSubscriptionTierException for an unknown tier.
  Result<SubscriptionPlan> GetPlan(string tier);
}
