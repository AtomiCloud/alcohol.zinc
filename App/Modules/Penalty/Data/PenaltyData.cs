using System.ComponentModel.DataAnnotations;
using App.Modules.Charities.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.Users.Data;

namespace App.Modules.Penalty.Data;

public class PenaltyData
{
  [Key]
  public Guid Id { get; set; }
  public required Guid HabitExecutionId { get; set; } // UNIQUE idempotency key
  public required int AmountCents { get; set; }
  [MaxLength(8)]
  public required string Currency { get; set; }
  public required int Status { get; set; } // store PenaltyStatus as int
  [MaxLength(256)]
  public string? PaymentIntentId { get; set; }
  public int Attempts { get; set; } = 0;
  [MaxLength(2048)]
  public string? LastError { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Foreign Keys & Navigation
  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }
  public required Guid CharityId { get; set; }
  public virtual CharityData? Charity { get; set; }
  public virtual HabitExecutionData? HabitExecution { get; set; }
}
