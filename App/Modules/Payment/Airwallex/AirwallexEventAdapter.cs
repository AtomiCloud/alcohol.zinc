using Domain.Payment;

namespace App.Modules.Payment.Airwallex;

public class AirwallexEventAdapter
{
  // Process payment intent events (for habit execution payments)
  public (Guid, PaymentRecord, bool) ProcessPaymentIntentEvent(AirwallexEvent evt)
  {
    var id = evt.Data.Object.RequestId;
    var record = new PaymentRecord
    {
      Amount = evt.Data.Object.Amount,
      CapturedAmount = evt.Data.Object.CapturedAmount,
      Currency = evt.Data.Object.Currency,
      LastUpdated = DateTime.UtcNow,
      Status = evt.Data.Object.Status
    };
    var complete = evt.Data.Object.Status == "SUCCEEDED";
    return (id, record, complete);
  }

  // Process payment consent events (for consent verification). The purpose is
  // classified from the consent's MIT trigger reason: a consent created for
  // scheduled (recurring) charging belongs to subscriptions; anything else is
  // the penalty (unscheduled) consent — including legacy consents created
  // before the split, which carry no scheduled reason.
  public (string CustomerId, string? ConsentId, PaymentConsentStatus? Status, ConsentPurpose Purpose) ProcessPaymentConsentEvent(AirwallexEvent evt)
  {
    var customerId = evt.Data.Object.CustomerId;
    var consentId = evt.Data.Object.Id;
    var statusString = evt.Data.Object.Status;
    var purpose = string.Equals(evt.Data.Object.MerchantTriggerReason, "scheduled", StringComparison.OrdinalIgnoreCase)
      ? ConsentPurpose.Subscription
      : ConsentPurpose.Penalty;

    // Parse Airwallex status string to enum
    PaymentConsentStatus? status = statusString switch
    {
      "VERIFIED" => PaymentConsentStatus.Verified,
      "REQUIRES_PAYMENT_METHOD" => PaymentConsentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentConsentStatus.RequiresCustomerAction,
      _ => null
    };

    return (customerId, consentId, status, purpose);
  }
}
