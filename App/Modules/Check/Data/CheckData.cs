using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;

namespace App.Modules.Check.Data;

// Values mirror HabitExecutionStatusData for the settled states.
public enum HabitCheckStatusData : byte
{
  Pending = 0,
  Done = 1,
  Missed = 2,
  Skipped = 3,
  Frozen = 4,
  Vacation = 5
}

public class HabitCheckData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid HabitId { get; set; }
  public required DateOnly LocalDate { get; set; }

  [MaxLength(64)]
  public required string Timezone { get; set; }

  public required DateTime OpensAtUtc { get; set; }
  public required DateTime DeadlineUtc { get; set; }
  public required HabitCheckStatusData Status { get; set; }
  public DateTime? SettledAtUtc { get; set; }

  [MaxLength(64)]
  public string? SettledBy { get; set; }

  public Guid? ExecutionId { get; set; }

  public virtual HabitData? Habit { get; set; }
  public virtual HabitExecutionData? Execution { get; set; }
}

public class HabitEvidenceData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid HabitId { get; set; }
  public required Guid CheckId { get; set; }

  [MaxLength(64)]
  public required string ProofKey { get; set; }

  [MaxLength(256)]
  public required string ExternalId { get; set; }

  public required bool Passed { get; set; }
  public required DateTime OccurredAtUtc { get; set; }
  public required DateTime ReceivedAtUtc { get; set; }

  [MaxLength(4096)]
  public string? Data { get; set; }
}

// Outbox: one CheckSettled event per settle, written in the settle's transaction.
public class CheckEventData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid CheckId { get; set; }
  public required Guid HabitId { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  [MaxLength(32)]
  public required string Type { get; set; }

  public required HabitCheckStatusData Outcome { get; set; }
  public Guid? ExecutionId { get; set; }
  public required DateTime CreatedAtUtc { get; set; }
  public DateTime? DispatchedAtUtc { get; set; }
  public int Attempts { get; set; }
  public required DateTime NextAttemptAtUtc { get; set; }

  [MaxLength(1024)]
  public string? LastError { get; set; }
}

public class CheckEventDeliveryData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid EventId { get; set; }

  [MaxLength(64)]
  public required string BindingId { get; set; }

  public required DateTime DeliveredAtUtc { get; set; }
}
