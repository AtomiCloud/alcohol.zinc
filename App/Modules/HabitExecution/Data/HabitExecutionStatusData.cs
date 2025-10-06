namespace App.Modules.HabitExecution.Data;

// Data-layer status values are decoupled from domain enum and stored as a byte
public enum HabitExecutionStatusData : byte
{
  Unknown = 0,
  Completed = 1,
  Failed = 2,
  Skipped = 3,
  Frozen = 4,
  Vacation = 5
}

