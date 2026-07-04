using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means the user has no verified payment consent to charge the subscription fee against.")]
public class NoPaymentConsent : IDomainProblem
{
  public NoPaymentConsent() { }

  public NoPaymentConsent(string userId)
  {
    this.UserId = userId;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "no_payment_consent";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "No Payment Consent";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "Set up a payment method before subscribing.";

  [Description("The user without a verified payment consent")]
  public string UserId { get; } = string.Empty;
}

[Description("This error means the user already has an active subscription.")]
public class AlreadySubscribed : IDomainProblem
{
  public AlreadySubscribed() { }

  public AlreadySubscribed(string currentTier, string requestedTier)
  {
    this.CurrentTier = currentTier;
    this.RequestedTier = requestedTier;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "already_subscribed";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Already Subscribed";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = string.Empty;

  [Description("The tier the user is currently subscribed to")]
  public string CurrentTier { get; } = string.Empty;

  [Description("The tier that was requested")]
  public string RequestedTier { get; } = string.Empty;
}

[Description("This error means the requested subscription tier does not exist or cannot be subscribed to.")]
public class InvalidSubscriptionTier : IDomainProblem
{
  public InvalidSubscriptionTier() { }

  public InvalidSubscriptionTier(string tier)
  {
    this.Tier = tier;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "invalid_subscription_tier";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Invalid Subscription Tier";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = string.Empty;

  [Description("The tier that was requested")]
  public string Tier { get; } = string.Empty;
}

[Description("This error means the subscription charge did not succeed; no tier change was applied.")]
public class SubscriptionChargeFailed : IDomainProblem
{
  public SubscriptionChargeFailed() { }

  public SubscriptionChargeFailed(string intentStatus)
  {
    this.IntentStatus = intentStatus;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "subscription_charge_failed";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Subscription Charge Failed";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "The payment was not completed; retry after checking the payment method.";

  [Description("The status of the payment intent (e.g., REQUIRES_PAYMENT_METHOD)")]
  public string IntentStatus { get; } = string.Empty;
}

[Description("This error means a renewal charge is currently in progress for this subscription; retry shortly.")]
public class RenewalInProgress : IDomainProblem
{
  public RenewalInProgress() { }

  public RenewalInProgress(string userId)
  {
    this.UserId = userId;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "renewal_in_progress";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Renewal In Progress";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "The subscription is being renewed; retry in a few minutes.";

  [Description("The user whose subscription is being renewed")]
  public string UserId { get; } = string.Empty;
}

[Description("This error means the user has no active subscription to act on.")]
public class NoActiveSubscription : IDomainProblem
{
  public NoActiveSubscription() { }

  public NoActiveSubscription(string userId)
  {
    this.UserId = userId;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "no_active_subscription";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "No Active Subscription";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = string.Empty;

  [Description("The user without an active subscription")]
  public string UserId { get; } = string.Empty;
}
