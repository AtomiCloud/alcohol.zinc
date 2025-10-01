using System.Text.Json;

namespace Domain.Payment;

public record PaymentCustomerSearch
{
  public string? UserId { get; init; }
  public string? AirwallexCustomerId { get; init; }
  public bool? HasPaymentConsent { get; init; }
  public DateTime? CreatedBefore { get; init; }
  public DateTime? CreatedAfter { get; init; }
  public int Limit { get; init; }
  public int Skip { get; init; }
}

public record PaymentCustomer
{
  public required PaymentCustomerPrincipal Principal { get; init; }
}

public record PaymentCustomerPrincipal
{
  public required Guid Id { get; init; }
  public required PaymentCustomerRecord Record { get; init; }
  public required DateTime CreatedAt { get; init; }
  public required DateTime UpdatedAt { get; init; }
}

public record PaymentCustomerRecord
{
  public required string UserId { get; init; }
  public required string AirwallexCustomerId { get; init; }
  public required bool HasPaymentConsent { get; init; }
  public required JsonDocument AdditionalData { get; init; }
}

public record PaymentConsentInfo
{
  public required string Id { get; init; }
  public required string Status { get; init; }
  public required string Currency { get; init; }
  public required string NextTriggeredBy { get; init; }
  public required JsonDocument AdditionalData { get; init; }
}

public record PaymentIntentResult
{
  public required string Id { get; init; }
  public required string Status { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
  public required string CustomerId { get; init; }
  public required string MerchantOrderId { get; init; }
  public required JsonDocument AdditionalData { get; init; }
}

public record ClientSecretResult
{
  public required string ClientSecret { get; init; }
  public required string CustomerId { get; init; }
}

public record CreatePaymentIntentRequest
{
  public required string UserId { get; init; }
  public required string CustomerId { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
  public required string Description { get; init; }
}

public record ConfirmPaymentIntentRequest
{
  public required string UserId { get; init; }
  public required string PaymentIntentId { get; init; }
  public required string PaymentConsentId { get; init; }
}

public record PaymentRecord
{
  public required decimal Amount { get; init; }
  public required decimal CapturedAmount { get; init; }
  public required string Currency { get; init; }
  public required DateTime LastUpdated { get; init; }
  public required string Status { get; init; }
  public required JsonDocument AdditionalData { get; init; }
}
