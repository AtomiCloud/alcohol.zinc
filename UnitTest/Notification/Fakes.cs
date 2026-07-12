using Domain.Notification;
using NodaMoney;

namespace UnitTest.Notification;

// Hand-rolled IEmailNotifier fakes (no Moq, matching the repo convention).

/// <summary>Records every notification so tests can assert the email transition matrix.</summary>
public sealed class RecordingEmailNotifier : IEmailNotifier
{
  public List<(string UserId, Money Amount, Guid CharityId)> PenaltyCharged { get; } = [];
  public List<(string UserId, Money Amount, Guid CharityId)> PenaltyFailed { get; } = [];
  public List<(string UserId, string Tier, Money Amount)> SubscriptionPurchased { get; } = [];
  public List<(string UserId, string Tier, DateTime NextBilling)> SubscriptionRenewed { get; } = [];
  public List<(string UserId, string Tier, DateTime GraceEnds)> SubscriptionPaymentFailed { get; } = [];
  public List<(string UserId, string FromTier, string ToTier, DateTime Effective)> SubscriptionChanged { get; } = [];
  public List<(string UserId, string Tier)> SubscriptionEnded { get; } = [];
  public List<(string UserId, string Email, string Username)> Welcome { get; } = [];
  public List<(string UserId, bool Linked, string Purpose)> ConsentChanged { get; } = [];

  public Task NotifyPenaltyCharged(string userId, Money amount, Guid charityId, Guid habitExecutionId, DateTime chargedAtUtc)
  {
    PenaltyCharged.Add((userId, amount, charityId));
    return Task.CompletedTask;
  }

  public Task NotifyPenaltyFailed(string userId, Money amount, Guid charityId, Guid habitExecutionId, DateTime attemptedAtUtc)
  {
    PenaltyFailed.Add((userId, amount, charityId));
    return Task.CompletedTask;
  }

  public Task NotifySubscriptionPurchased(string userId, string tier, Money amount, DateTime nextBillingUtc)
  {
    SubscriptionPurchased.Add((userId, tier, amount));
    return Task.CompletedTask;
  }

  public Task NotifySubscriptionRenewed(string userId, string tier, DateTime nextBillingUtc)
  {
    SubscriptionRenewed.Add((userId, tier, nextBillingUtc));
    return Task.CompletedTask;
  }

  public Task NotifySubscriptionPaymentFailed(string userId, string tier, DateTime graceEndsUtc)
  {
    SubscriptionPaymentFailed.Add((userId, tier, graceEndsUtc));
    return Task.CompletedTask;
  }

  public Task NotifySubscriptionChanged(string userId, string fromTier, string toTier, DateTime effectiveUtc)
  {
    SubscriptionChanged.Add((userId, fromTier, toTier, effectiveUtc));
    return Task.CompletedTask;
  }

  public Task NotifySubscriptionEnded(string userId, string tier, DateTime endedAtUtc)
  {
    SubscriptionEnded.Add((userId, tier));
    return Task.CompletedTask;
  }

  public Task NotifyWelcome(string userId, string email, string username)
  {
    Welcome.Add((userId, email, username));
    return Task.CompletedTask;
  }

  public Task NotifyConsentChanged(string userId, bool linked, string purpose, DateTime changedAtUtc)
  {
    ConsentChanged.Add((userId, linked, purpose));
    return Task.CompletedTask;
  }
}

/// <summary>
/// Violates the never-throw contract on purpose: proves call sites do not let
/// an email failure alter a money result. (The REAL implementation never
/// throws; this guards the call sites against a regression in that impl.)
/// </summary>
public sealed class ThrowingEmailNotifier : IEmailNotifier
{
  private Task Boom() => throw new InvalidOperationException("email infrastructure down");

  public Task NotifyPenaltyCharged(string userId, Money amount, Guid charityId, Guid habitExecutionId, DateTime chargedAtUtc) => this.Boom();
  public Task NotifyPenaltyFailed(string userId, Money amount, Guid charityId, Guid habitExecutionId, DateTime attemptedAtUtc) => this.Boom();
  public Task NotifySubscriptionPurchased(string userId, string tier, Money amount, DateTime nextBillingUtc) => this.Boom();
  public Task NotifySubscriptionRenewed(string userId, string tier, DateTime nextBillingUtc) => this.Boom();
  public Task NotifySubscriptionPaymentFailed(string userId, string tier, DateTime graceEndsUtc) => this.Boom();
  public Task NotifySubscriptionChanged(string userId, string fromTier, string toTier, DateTime effectiveUtc) => this.Boom();
  public Task NotifySubscriptionEnded(string userId, string tier, DateTime endedAtUtc) => this.Boom();
  public Task NotifyWelcome(string userId, string email, string username) => this.Boom();
  public Task NotifyConsentChanged(string userId, bool linked, string purpose, DateTime changedAtUtc) => this.Boom();
}
