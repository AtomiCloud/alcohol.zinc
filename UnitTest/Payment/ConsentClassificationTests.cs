using App.Modules.Payment.Airwallex;
using Domain.Payment;

namespace UnitTest.Payment;

// Webhook consent events are classified into a purpose from the consent's MIT
// trigger reason: "scheduled" (recurring) belongs to subscriptions; anything
// else — including legacy consents created before the split — is the penalty
// (unscheduled) consent.
public class ConsentClassificationTests
{
  private static AirwallexEvent ConsentEvent(string triggerReason) => new()
  {
    Name = "payment_consent.verified",
    Data = new AirwallexEventData
    {
      Object = new AirwallexEventDataObject
      {
        Id = "cst_1",
        CustomerId = "cus_1",
        Status = "VERIFIED",
        MerchantTriggerReason = triggerReason
      }
    }
  };

  [Theory]
  [InlineData("scheduled", ConsentPurpose.Subscription)]
  [InlineData("SCHEDULED", ConsentPurpose.Subscription)]
  [InlineData("unscheduled", ConsentPurpose.Penalty)]
  [InlineData("", ConsentPurpose.Penalty)] // legacy consents carry no reason
  public void ConsentEvent_ClassifiedByTriggerReason(string reason, ConsentPurpose expected)
  {
    var adapter = new AirwallexEventAdapter();

    var (customerId, consentId, status, purpose) = adapter.ProcessPaymentConsentEvent(ConsentEvent(reason));

    customerId.Should().Be("cus_1");
    consentId.Should().Be("cst_1");
    status.Should().Be(PaymentConsentStatus.Verified);
    purpose.Should().Be(expected);
  }
}
