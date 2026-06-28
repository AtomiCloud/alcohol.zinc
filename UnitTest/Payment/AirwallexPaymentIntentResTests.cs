using App.Modules.Payment.Airwallex;
using App.Utility;
using FluentAssertions;
using Xunit;

namespace UnitTest.Payment;

// Regression: an off-session MIT confirm response omits client_secret (and may omit
// request_id / captured_amount). The shared AirwallexPaymentIntentRes must parse without
// them — requiring them threw a JsonException that crashed the charge AFTER the money had
// already moved at Airwallex. Uses the real Utils.ToObj path the client uses.
public class AirwallexPaymentIntentResTests
{
  [Fact]
  public void ToObj_ConfirmResponseMissingOptionalFields_Parses()
  {
    // Shape of a real MIT confirm response: no client_secret / request_id / captured_amount.
    var json = """
      {
        "id": "int_hkdmsgmf8",
        "amount": 1.00,
        "currency": "USD",
        "merchant_order_id": "pen-1",
        "status": "SUCCEEDED",
        "customer_id": "cus_1"
      }
      """;

    var act = () => json.ToObj<AirwallexPaymentIntentRes>();

    act.Should().NotThrow();
    var res = json.ToObj<AirwallexPaymentIntentRes>();
    res.Id.Should().Be("int_hkdmsgmf8");
    res.Status.Should().Be("SUCCEEDED");
    res.CustomerId.Should().Be("cus_1");
    res.MerchantOrderId.Should().Be("pen-1");
    res.ClientSecret.Should().BeNull();   // absent on MIT confirm -> optional
    res.RequestId.Should().BeNull();
    res.CapturedAmount.Should().BeNull();
  }

  [Fact]
  public void ToObj_FullResponse_StillPopulatesOptionalFields()
  {
    var json = """
      {
        "id": "int_1", "request_id": "req_1", "amount": 2.50, "currency": "USD",
        "merchant_order_id": "mo_1", "status": "REQUIRES_PAYMENT_METHOD",
        "captured_amount": 0.00, "customer_id": "cus_1", "client_secret": "cs_1"
      }
      """;

    var res = json.ToObj<AirwallexPaymentIntentRes>();
    res.RequestId.Should().Be("req_1");
    res.ClientSecret.Should().Be("cs_1");
    res.CapturedAmount.Should().Be(0.00m);
  }
}
