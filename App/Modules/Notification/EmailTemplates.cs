namespace App.Modules.Notification;

/// <summary>
/// Template ids (embedded resource names under App.Templates.Email.templates)
/// paired with their subjects. Subjects live here — the TSX subject/preview
/// strings are not consumed server-side.
/// </summary>
public static class EmailTemplates
{
  public const string PenaltyCharged = "penalty-charged";
  public const string PenaltyChargedSubject = "Your stake went to charity";

  public const string PenaltyPaymentFailed = "penalty-payment-failed";
  public const string PenaltyPaymentFailedSubject = "We couldn't process your stake";

  public const string SubscriptionReceipt = "subscription-receipt";
  public const string SubscriptionReceiptSubject = "Your LazyTax receipt";

  public const string SubscriptionPaymentFailed = "subscription-payment-failed";
  public const string SubscriptionPaymentFailedSubject = "Payment failed — your plan needs attention";

  public const string SubscriptionCancelled = "subscription-cancelled";
  public const string SubscriptionCancelledSubject = "Your subscription change is confirmed";

  public const string Welcome = "welcome";
  public const string WelcomeSubject = "Welcome to LazyTax — let's build habits that stick";

  public const string PaymentConsentChanged = "payment-consent-changed";
  public const string PaymentConsentChangedSubject = "Your payment method changed";

  public const string MemberThankYou = "member-thank-you";
  public const string MemberThankYouSubject = "Thank you for being a LazyTax member";
}
