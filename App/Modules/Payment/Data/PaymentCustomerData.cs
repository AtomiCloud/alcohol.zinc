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

  public required DateTime CreatedAt { get; set; }

  public required DateTime UpdatedAt { get; set; }
}