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
}