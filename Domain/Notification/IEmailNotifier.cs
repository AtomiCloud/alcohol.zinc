using NodaMoney;

namespace Domain.Notification;

/// <summary>
/// Best-effort transactional email notifications for money events.
///
/// CONTRACT: implementations MUST NEVER throw and MUST NOT surface failures to
/// callers — an email can never fail, roll back, or delay a charge, renewal,
/// or signup. Callers await these AFTER the money write has succeeded.
/// </summary>
public interface IEmailNotifier
{
  Task NotifyPenaltyCharged(string userId, Money amount, Guid charityId, DateTime chargedAtUtc);
  Task NotifyPenaltyFailed(string userId, Money amount, Guid charityId, DateTime attemptedAtUtc);

  /// <summary>First purchase or immediate upgrade receipt.</summary>
  Task NotifySubscriptionPurchased(string userId, string tier, Money amount, DateTime nextBillingUtc);

  /// <summary>Successful renewal receipt (amount resolved from the plan).</summary>
  Task NotifySubscriptionRenewed(string userId, string tier, DateTime nextBillingUtc);

  /// <summary>Renewal charge failed; subscription entered its grace window.</summary>
  Task NotifySubscriptionPaymentFailed(string userId, string tier, DateTime graceEndsUtc);

  /// <summary>User-requested cancel or scheduled downgrade confirmation.</summary>
  Task NotifySubscriptionChanged(string userId, string fromTier, string toTier, DateTime effectiveUtc);

  /// <summary>Grace window exhausted — subscription lapsed to free.</summary>
  Task NotifySubscriptionEnded(string userId, string tier, DateTime endedAtUtc);

  /// <summary>After first sign-up; email passed in because the row may not be readable pre-commit.</summary>
  Task NotifyWelcome(string userId, string email, string username);

  /// <summary>Payment consent linked (true) or revoked (false) for a purpose ("Habit stakes"/"Subscription").</summary>
  Task NotifyConsentChanged(string userId, bool linked, string purpose, DateTime changedAtUtc);
}
