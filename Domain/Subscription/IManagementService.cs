using CSharp_Result;

namespace Domain.Subscription;

public interface ISubscriptionManagementService
{
  // New subscription OR immediate tier switch (charges the full new-tier price
  // now and resets the period; no proration). Re-subscribing the same tier
  // while cancel-at-period-end is pending un-cancels instead of charging.
  Task<Result<UserSubscriptionPrincipal>> Subscribe(string userId, string tier);

  // Sets cancel-at-period-end; paid tier is retained until the period rolls.
  Task<Result<UserSubscriptionPrincipal>> Cancel(string userId);

  // "free" -> Cancel; higher-priced tier -> Subscribe (immediate); lower-priced
  // paid tier -> scheduled downgrade applied at the next renewal.
  Task<Result<UserSubscriptionPrincipal>> ChangeTier(string userId, string tier);

  // Renewal worker entrypoint: charges due subscriptions, applies grace and
  // cancellation semantics. Returns the number of rows brought to a terminal
  // state this pass (renewed or cancelled).
  Task<Result<int>> ProcessRenewals(DateTime nowUtc, int batchSize);

}
