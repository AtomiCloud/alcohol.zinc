using System.ComponentModel.DataAnnotations;
using Domain.Payment;

namespace App.Modules.Payment.Data;

public class PaymentIntentData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  [MaxLength(128)]
  public required string AirwallexPaymentIntentId { get; set; }

  [MaxLength(128)]
  public required string AirwallexCustomerId { get; set; }

  public required long AmountCents { get; set; }

  [MaxLength(3)]
  public required string Currency { get; set; }

  public required long CapturedAmountCents { get; set; }

  public required PaymentIntentStatus Status { get; set; }
  
  [MaxLength(128)]
  public required string MerchantOrderId { get; set; }

  public required DateTime CreatedAt { get; set; }

  public required DateTime UpdatedAt { get; set; }

  // Navigation property for many-to-many relationship with HabitExecutions
  public virtual ICollection<PaymentIntentExecutionData> PaymentIntentExecutions { get; set; } = [];
}
