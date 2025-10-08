using System.Text.Json;
using Domain.Payment;

namespace App.Modules.Payment.Airwallex;

public class AirwallexEventAdapter
{
  // Process payment intent events (for habit execution payments)
  public (string PaymentIntentId, PaymentIntentStatus Status, decimal CapturedAmount) ProcessPaymentIntentEvent(AirwallexEvent evt)
  {
    var intentObj = evt.Data.Object.Deserialize<PaymentIntentEventObject>()
      ?? throw new InvalidOperationException("Failed to deserialize PaymentIntentEventObject");

    // Parse Airwallex status string to enum
    PaymentIntentStatus status = intentObj.Status switch
    {
      "REQUIRES_PAYMENT_METHOD" => PaymentIntentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentIntentStatus.RequiresCustomerAction,
      "REQUIRES_CAPTURE" => PaymentIntentStatus.RequiresCapture,
      "PENDING" => PaymentIntentStatus.Pending,
      "SUCCEEDED" => PaymentIntentStatus.Succeeded,
      "CANCELLED" => PaymentIntentStatus.Cancelled,
      _ => throw new ArgumentException($"Unknown payment intent status: {intentObj.Status}")
    };

    return (intentObj.Id, status, intentObj.CapturedAmount);
  }

  // Process payment consent events (for consent verification)
  public (string CustomerId, string? ConsentId, PaymentConsentStatus? Status) ProcessPaymentConsentEvent(AirwallexEvent evt)
  {
    var consentObj = evt.Data.Object.Deserialize<PaymentConsentEventObject>()
      ?? throw new InvalidOperationException("Failed to deserialize PaymentConsentEventObject");

    // Parse Airwallex status string to enum
    PaymentConsentStatus? status = consentObj.Status switch
    {
      "VERIFIED" => PaymentConsentStatus.Verified,
      "REQUIRES_PAYMENT_METHOD" => PaymentConsentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentConsentStatus.RequiresCustomerAction,
      _ => null
    };

    return (consentObj.CustomerId, consentObj.Id, status);
  }
}
