namespace Domain.Subscription;

// Domain-level typed exceptions for the subscription flows; the App layer maps
// them to HTTP problems in AtomiControllerBase.MapException (same approach as
// AccountDeletionBlockedException).

public class InvalidSubscriptionTierException(string tier)
  : Exception($"'{tier}' is not a subscribable tier")
{
  public string Tier { get; } = tier;
}

public class NoPaymentConsentException(string userId)
  : Exception($"User {userId} has no verified payment consent")
{
  public string UserId { get; } = userId;
}

public class AlreadySubscribedException(string currentTier, string requestedTier)
  : Exception($"Already subscribed to '{currentTier}' (requested '{requestedTier}')")
{
  public string CurrentTier { get; } = currentTier;
  public string RequestedTier { get; } = requestedTier;
}

public class SubscriptionChargeFailedException(string intentStatus)
  : Exception($"Subscription charge did not succeed (intent status: {intentStatus})")
{
  public string IntentStatus { get; } = intentStatus;
}

public class NoActiveSubscriptionException(string userId)
  : Exception($"User {userId} has no active subscription")
{
  public string UserId { get; } = userId;
}

public class SubscriptionBusyException(string userId)
  : Exception($"A renewal charge for user {userId} is in progress; retry shortly")
{
  public string UserId { get; } = userId;
}
