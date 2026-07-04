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

  // Purpose-scoped consents (penalty / subscription) live in their own table.
  public virtual ICollection<PaymentConsentData> Consents { get; set; } = [];

  public required DateTime CreatedAt { get; set; }

  public required DateTime UpdatedAt { get; set; }
}