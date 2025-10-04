namespace App.Modules.Payment.API.V1;

// Request Models

public record CreatePaymentIntentReq
{
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
  public required string Description { get; init; }
}

public record ConfirmPaymentIntentReq
{
  public required string PaymentConsentId { get; init; }
}

// Response Models

public record CreateCustomerRes
{
  public required string CustomerId { get; init; }
  public required string ClientSecret { get; init; }
}

public record ClientSecretRes
{
  public required string ClientSecret { get; init; }
  public required string CustomerId { get; init; }
}

public record PaymentConsentRes
{
  public required bool HasPaymentConsent { get; init; }
  public required string? ConsentId { get; init; }
  public required string? Status { get; init; }
}

public record CreatePaymentIntentRes
{
  public required string PaymentIntentId { get; init; }
  public required string Status { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
}

public record ConfirmPaymentIntentRes
{
  public required string PaymentIntentId { get; init; }
  public required string Status { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
}