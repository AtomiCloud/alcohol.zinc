using System.ComponentModel.DataAnnotations;

namespace App.Modules.Payment.Data;

public class PaymentCustomerData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  [MaxLength(128)]
  public required string AirwallexCustomerId { get; set; }

  [MaxLength(128)]
  public string? PaymentConsentId { get; set; }

  [MaxLength(50)]
  public string? PaymentConsentStatus { get; set; }

  // Recurring-MIT consent used for subscription renewals; the columns above
  // remain the unscheduled-MIT consent used for penalties.
  [MaxLength(128)]
  public string? SubscriptionConsentId { get; set; }

  [MaxLength(50)]
  public string? SubscriptionConsentStatus { get; set; }

  public required DateTime CreatedAt { get; set; }

  public required DateTime UpdatedAt { get; set; }
}