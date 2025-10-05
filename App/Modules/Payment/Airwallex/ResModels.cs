using System.Text.Json.Serialization;

namespace App.Modules.Payment.Airwallex;

public record AirwallexAuthTokenRes
{
  [JsonPropertyName("expires_at")]
  public required string ExpiresAt { get; init; }

  [JsonPropertyName("token")]
  public required string Token { get; init; }
}

public record AirwallexCreateCustomerRes
{
  [JsonPropertyName("id")]
  public required string Id { get; init; }

  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("merchant_customer_id")]
  public required string MerchantCustomerId { get; init; }
}

public record AirwallexListCustomersRes
{
  [JsonPropertyName("items")]
  public required AirwallexCustomerItem[] Items { get; init; }
}

public record AirwallexCustomerItem
{
  [JsonPropertyName("id")]
  public required string Id { get; init; }

  [JsonPropertyName("merchant_customer_id")]
  public required string MerchantCustomerId { get; init; }
}

public record AirwallexClientSecretRes
{
  [JsonPropertyName("client_secret")]
  public required string ClientSecret { get; init; }

  [JsonPropertyName("expired_time")]
  public string? ExpiredTime { get; init; }
}

public record AirwallexPaymentConsentsRes
{
  [JsonPropertyName("items")]
  public required AirwallexPaymentConsentItem[] Items { get; init; }
}

public record AirwallexPaymentConsentItem
{
  [JsonPropertyName("id")]
  public required string Id { get; init; }

  [JsonPropertyName("status")]
  public required string Status { get; init; }

  [JsonPropertyName("currency")]
  public required string Currency { get; init; }

  [JsonPropertyName("next_triggered_by")]
  public required string NextTriggeredBy { get; init; }
}

public record AirwallexPaymentIntentRes
{
  [JsonPropertyName("id")]
  public required string Id { get; init; }

  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("amount")]
  public required decimal Amount { get; init; }

  [JsonPropertyName("currency")]
  public required string Currency { get; init; }

  [JsonPropertyName("merchant_order_id")]
  public required string MerchantOrderId { get; init; }

  [JsonPropertyName("status")]
  public required string Status { get; init; }

  [JsonPropertyName("captured_amount")]
  public required decimal CapturedAmount { get; init; }

  [JsonPropertyName("customer_id")]
  public required string CustomerId { get; init; }

  [JsonPropertyName("client_secret")]
  public required string ClientSecret { get; init; }
}

public record AirwallexPaymentConsentRes
{
  [JsonPropertyName("id")]
  public required string Id { get; init; }

  [JsonPropertyName("request_id")]
  public required string RequestId { get; init; }

  [JsonPropertyName("customer_id")]
  public required string CustomerId { get; init; }

  [JsonPropertyName("status")]
  public required string Status { get; init; }
}