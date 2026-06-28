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

  // NOTE: do NOT send a top-level "triggered_by" here. For a consent-based MIT charge
  // Airwallex derives merchant-vs-customer from the consent's next_triggered_by (set to
  // "merchant" when the card was saved). Sending triggered_by makes Airwallex demand a
  // payment_method.id and reject the confirm with a validation_error.

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
