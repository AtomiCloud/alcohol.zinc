using Domain.Notification;
using NodaMoney;

namespace IntTest.Notification;

// Integration tests exercise money persistence, not email delivery.
public sealed class NoopEmailNotifier : IEmailNotifier
{
  public Task NotifyPenaltyCharged(string userId, Money amount, Guid charityId, DateTime chargedAtUtc) => Task.CompletedTask;
  public Task NotifyPenaltyFailed(string userId, Money amount, Guid charityId, DateTime attemptedAtUtc) => Task.CompletedTask;
  public Task NotifySubscriptionPurchased(string userId, string tier, Money amount, DateTime nextBillingUtc) => Task.CompletedTask;
  public Task NotifySubscriptionRenewed(string userId, string tier, DateTime nextBillingUtc) => Task.CompletedTask;
  public Task NotifySubscriptionPaymentFailed(string userId, string tier, DateTime graceEndsUtc) => Task.CompletedTask;
  public Task NotifySubscriptionChanged(string userId, string fromTier, string toTier, DateTime effectiveUtc) => Task.CompletedTask;
  public Task NotifySubscriptionEnded(string userId, string tier, DateTime endedAtUtc) => Task.CompletedTask;
  public Task NotifyWelcome(string userId, string email, string username) => Task.CompletedTask;
  public Task NotifyConsentChanged(string userId, bool linked, string purpose, DateTime changedAtUtc) => Task.CompletedTask;
}
