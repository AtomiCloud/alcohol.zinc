namespace Domain.Payment;

public enum PaymentConsentStatus
{
  Verified,
  RequiresPaymentMethod,
  RequiresCustomerAction
}

// Which charging relationship a stored consent authorizes, mirroring the card
// networks' MIT classification. Kept separate per user so disputes, approval
// scoring and revocation are scoped to the right agreement:
//   Penalty      -> unscheduled merchant-initiated (event-driven, variable)
//   Subscription -> recurring merchant-initiated (fixed cadence)
public enum ConsentPurpose
{
  Penalty,
  Subscription
}

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

// One stored consent (a purpose-scoped Airwallex mandate) of a customer.
public record StoredPaymentConsent
{
  public required string ConsentId { get; init; }
  public PaymentConsentStatus? Status { get; init; }
  public bool Verified => this.Status == PaymentConsentStatus.Verified;
}

public record PaymentCustomerRecord
{
  public required string UserId { get; init; }
  public required string AirwallexCustomerId { get; init; }

  // At most one consent per purpose (unique (customer, purpose) in storage).
  public IReadOnlyDictionary<ConsentPurpose, StoredPaymentConsent> Consents { get; init; }
    = new Dictionary<ConsentPurpose, StoredPaymentConsent>();

  public string? ConsentIdFor(ConsentPurpose purpose)
    => this.Consents.TryGetValue(purpose, out var c) ? c.ConsentId : null;

  public PaymentConsentStatus? ConsentStatusFor(ConsentPurpose purpose)
    => this.Consents.TryGetValue(purpose, out var c) ? c.Status : null;

  public bool HasConsentFor(ConsentPurpose purpose)
    => this.Consents.TryGetValue(purpose, out var c) && c.Verified;

  // Compatibility conveniences (penalty = the original single consent).
  public string? PaymentConsentId => this.ConsentIdFor(ConsentPurpose.Penalty);
  public PaymentConsentStatus? ConsentStatus => this.ConsentStatusFor(ConsentPurpose.Penalty);
  public bool HasPaymentConsent => this.HasConsentFor(ConsentPurpose.Penalty);
  public bool HasSubscriptionConsent => this.HasConsentFor(ConsentPurpose.Subscription);
}

public record PaymentConsentInfo
{
  public required string Id { get; init; }
  public required string Status { get; init; }
  public required string Currency { get; init; }
  public required string NextTriggeredBy { get; init; }
}

public record PaymentConsentStatusResult
{
  public string? ConsentId { get; init; }
  public PaymentConsentStatus? Status { get; init; }
  public required bool HasPaymentConsent { get; init; }
}

public record PaymentIntentResult
{
  public required string Id { get; init; }
  public required string Status { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
  public required string CustomerId { get; init; }
  public required string MerchantOrderId { get; init; }
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

  // Optional deterministic idempotency key. When set, the gateway uses it as the
  // Airwallex request_id AND merchant_order_id so a retried/concurrent attempt for
  // the same logical charge collapses to the same intent instead of creating a new
  // one. Penalty drains derive this from the penalty Id. Interactive callers leave
  // it null and the gateway mints fresh GUIDs (legacy behaviour).
  public string? IdempotencyKey { get; init; }
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
}
