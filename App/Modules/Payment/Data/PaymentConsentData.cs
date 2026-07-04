using System.ComponentModel.DataAnnotations;

namespace App.Modules.Payment.Data;

// One purpose-scoped Airwallex consent (mandate) of a payment customer.
// At most one row per (customer, purpose) — enforced by a unique index.
public class PaymentConsentData
{
  [Key]
  public Guid Id { get; set; }

  public required int Purpose { get; set; } // Domain.Payment.ConsentPurpose as int

  [MaxLength(128)]
  public required string ConsentId { get; set; }

  [MaxLength(50)]
  public string? Status { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Foreign Keys & Navigation
  public required Guid PaymentCustomerId { get; set; }

  public virtual PaymentCustomerData? PaymentCustomer { get; set; }
}
