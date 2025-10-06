using System.Text.Json.Serialization;

namespace App.Modules.Payment.Airwallex;

public record AirwallexEvent
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("account_id")]
  public string AirwallexEventAccountId { get; set; } = string.Empty;

  [JsonPropertyName("accountId")]
  public string AccountId { get; set; } = string.Empty;

  [JsonPropertyName("data")]
  public AirwallexEventData Data { get; set; } = new();

  [JsonPropertyName("created_at")]
  public string AirwallexEventCreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("createdAt")]
  public string CreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("sourceId")]
  public string SourceId { get; set; } = string.Empty;
}

public record AirwallexEventData
{
  [JsonPropertyName("object")]
  public AirwallexEventDataObject Object { get; set; } = new();
}

public record AirwallexEventDataObject
{
  // Payment Intent fields
  [JsonPropertyName("amount")]
  public decimal Amount { get; set; }

  [JsonPropertyName("base_amount")]
  public decimal BaseAmount { get; set; }

  [JsonPropertyName("base_currency")]
  public string BaseCurrency { get; set; } = string.Empty;

  [JsonPropertyName("captured_amount")]
  public decimal CapturedAmount { get; set; }

  [JsonPropertyName("merchant_order_id")]
  public string MerchantOrderId { get; set; } = string.Empty;

  [JsonPropertyName("request_id")]
  public Guid RequestId { get; set; }

  // Common fields
  [JsonPropertyName("created_at")]
  public string CreatedAt { get; set; } = string.Empty;

  [JsonPropertyName("updated_at")]
  public string UpdatedAt { get; set; } = string.Empty;

  [JsonPropertyName("currency")]
  public string Currency { get; set; } = string.Empty;

  [JsonPropertyName("descriptor")]
  public string Descriptor { get; set; } = string.Empty;

  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty;

  [JsonPropertyName("customer_id")]
  public string CustomerId { get; set; } = string.Empty;

  // Payment Consent specific fields
  [JsonPropertyName("initial_payment_intent_id")]
  public string InitialPaymentIntentId { get; set; } = string.Empty;

  [JsonPropertyName("merchant_trigger_reason")]
  public string MerchantTriggerReason { get; set; } = string.Empty;

  [JsonPropertyName("next_triggered_by")]
  public string NextTriggeredBy { get; set; } = string.Empty;

  [JsonPropertyName("purpose")]
  public string Purpose { get; set; } = string.Empty;
}