using App.StartUp.Registry;
using FluentValidation;

namespace App.Modules.Subscription.API.V1;

public class SubscribeReqValidator : AbstractValidator<SubscribeReq>
{
  public SubscribeReqValidator()
  {
    // Only paid tiers can be subscribed to; "free" is reached via cancel.
    RuleFor(x => x.Tier)
      .NotEmpty()
      .Must(t => t is SubscriptionTiers.Pro or SubscriptionTiers.Ultimate)
      .WithMessage($"Tier must be one of: {SubscriptionTiers.Pro}, {SubscriptionTiers.Ultimate}");
  }
}

public class ChangeTierReqValidator : AbstractValidator<ChangeTierReq>
{
  public ChangeTierReqValidator()
  {
    // "free" is allowed here: it means cancel at period end.
    RuleFor(x => x.Tier)
      .NotEmpty()
      .Must(t => t is SubscriptionTiers.Free or SubscriptionTiers.Pro or SubscriptionTiers.Ultimate)
      .WithMessage($"Tier must be one of: {SubscriptionTiers.Free}, {SubscriptionTiers.Pro}, {SubscriptionTiers.Ultimate}");
  }
}
