using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Modules.Payment.Airwallex;

// Base event structure (common to all webhook events)
public record AirwallexEvent
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("account_id")]
  public string AccountId { get; set; } = string.Empty;

  [JsonPropertyName("data")]
  public AirwallexEventData Data { get; set; } = new();

  [JsonPropertyName("created_at")]
  public string CreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("version")]
  public string Version { get; set; } = string.Empty;
}

public record AirwallexEventData
{
  [JsonPropertyName("object")]
  public JsonElement Object { get; set; }
}

// Payment Intent webhook object
public record PaymentIntentEventObject
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty;

  [JsonPropertyName("created_at")]
  public string CreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("updated_at")]
  public string UpdatedAt { get; set; } = string.Empty;

  [JsonPropertyName("request_id")]
  public string RequestId { get; set; } = string.Empty;

  [JsonPropertyName("amount")]
  public decimal Amount { get; set; }

  [JsonPropertyName("original_amount")]
  public decimal OriginalAmount { get; set; }

  [JsonPropertyName("original_currency")]
  public string OriginalCurrency { get; set; } = string.Empty;

  [JsonPropertyName("captured_amount")]
  public decimal CapturedAmount { get; set; }

  [JsonPropertyName("currency")]
  public string Currency { get; set; } = string.Empty;

  [JsonPropertyName("descriptor")]
  public string Descriptor { get; set; } = string.Empty;

  [JsonPropertyName("merchant_order_id")]
  public string MerchantOrderId { get; set; } = string.Empty;
}

// Payment Consent webhook object
public record PaymentConsentEventObject
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty;

  [JsonPropertyName("created_at")]
  public string CreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("updated_at")]
  public string UpdatedAt { get; set; } = string.Empty;

  [JsonPropertyName("request_id")]
  public string RequestId { get; set; } = string.Empty;

  [JsonPropertyName("customer_id")]
  public string CustomerId { get; set; } = string.Empty;

  [JsonPropertyName("initial_payment_intent_id")]
  public string InitialPaymentIntentId { get; set; } = string.Empty;

  [JsonPropertyName("merchant_trigger_reason")]
  public string MerchantTriggerReason { get; set; } = string.Empty;

  [JsonPropertyName("next_triggered_by")]
  public string NextTriggeredBy { get; set; } = string.Empty;

  [JsonPropertyName("purpose")]
  public string Purpose { get; set; } = string.Empty;
}