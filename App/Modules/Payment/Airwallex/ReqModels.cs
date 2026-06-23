using System.Text.Json.Serialization;

namespace App.Modules.Payment.Airwallex;

public record AirwallexCreateCustomerReq
{
  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("merchant_customer_id")]
  public required string MerchantCustomerId { get; init; }
}

public record AirwallexCreatePaymentIntentReq
{
  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("amount")]
  public required decimal Amount { get; init; }

  [JsonPropertyName("currency")]
  public required string Currency { get; init; }

  [JsonPropertyName("customer_id")]
  public required string CustomerId { get; init; }

  [JsonPropertyName("merchant_order_id")]
  public required string MerchantOrderId { get; init; }
}

public record AirwallexConfirmPaymentIntentReq
{
  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("payment_consent_id")]
  public required string PaymentConsentId { get; init; }

  [JsonPropertyName("customer_id")]
  public required string CustomerId { get; init; }

  // Off-session merchant-initiated transaction (MIT): the penalty is charged by a
  // background job with no customer present. Without "merchant" Airwallex treats it
  // as customer-initiated and can demand customer action (e.g. 3DS), which can never
  // settle off-session. The scheduled/unscheduled distinction lives on the consent
  // (next_triggered_by), not here.
  [JsonPropertyName("triggered_by")]
  public string TriggeredBy { get; init; } = "merchant";

  // Airwallex requires a syntactically valid return_url even though an off-session
  // MIT charge never redirects.
  [JsonPropertyName("return_url")]
  public required string ReturnUrl { get; init; }

  [JsonPropertyName("payment_method_options")]
  public AirwallexPaymentMethodOptions PaymentMethodOptions { get; init; } = new();
}

public record AirwallexPaymentMethodOptions
{
  [JsonPropertyName("card")]
  public AirwallexCardOptions Card { get; init; } = new();
}

public record AirwallexCardOptions
{
  // final_auth + auto_capture: capture the funds immediately rather than only
  // authorizing, so a successful confirm actually moves the money.
  [JsonPropertyName("authorization_type")]
  public string AuthorizationType { get; init; } = "final_auth";

  [JsonPropertyName("auto_capture")]
  public bool AutoCapture { get; init; } = true;
}
