using System.Text.Json;
using App.Modules.Payment.Airwallex;
using FluentAssertions;
using Xunit;

namespace UnitTest.Payment;

// Regression: a consent-based MIT confirm must NOT send a top-level triggered_by.
// Airwallex derives merchant-vs-customer from the consent's next_triggered_by; sending
// triggered_by makes it demand a payment_method.id and reject with a validation_error.
public class AirwallexConfirmReqTests
{
  [Fact]
  public void ConfirmRequest_OmitsTriggeredBy_KeepsConsentAndRequestId()
  {
    var req = new AirwallexConfirmPaymentIntentReq
    {
      RequestId = "confirm-int_1",
      PaymentConsentId = "cst_1",
      CustomerId = "cus_1",
      ReturnUrl = "https://atomi.cloud"
    };

    // Mirror the client's serialization (JsonContent.Create uses web defaults) so this
    // guards the real wire payload, not just default-option behavior.
    var json = JsonSerializer.Serialize(req, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    json.Should().NotContain("triggered_by");        // MIT comes from the consent, not here
    json.Should().Contain("\"payment_consent_id\":\"cst_1\"");
    json.Should().Contain("\"request_id\":\"confirm-int_1\"");
  }
}
