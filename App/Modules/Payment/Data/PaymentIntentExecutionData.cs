using System.ComponentModel.DataAnnotations;
using App.Modules.HabitExecution.Data;

namespace App.Modules.Payment.Data;

public class PaymentIntentExecutionData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid PaymentIntentId { get; set; }

  public required Guid HabitExecutionId { get; set; }

  public required DateTime CreatedAt { get; set; }

  // Navigation properties
  public virtual PaymentIntentData? PaymentIntent { get; set; }
  public virtual HabitExecutionData? HabitExecution { get; set; }
}