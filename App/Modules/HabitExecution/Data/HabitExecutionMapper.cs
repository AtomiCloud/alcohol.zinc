using Domain.Habit;

namespace App.Modules.HabitExecution.Data
{
  public static class HabitExecutionMapper
  {
    private static ExecutionStatus ToDomainStatus(this HabitExecutionStatusData s) => s switch
    {
      HabitExecutionStatusData.Completed => ExecutionStatus.Completed,
      HabitExecutionStatusData.Failed => ExecutionStatus.Failed,
      HabitExecutionStatusData.Skipped => ExecutionStatus.Skipped,
      HabitExecutionStatusData.Frozen => ExecutionStatus.Freeze,
      HabitExecutionStatusData.Vacation => ExecutionStatus.Vacation,
      _ => ExecutionStatus.Failed // defaulting unknown to failed is conservative for domain; adjust as needed
    };

    private static HabitExecutionStatusData ToDataStatus(this ExecutionStatus s) => s switch
    {
      ExecutionStatus.Completed => HabitExecutionStatusData.Completed,
      ExecutionStatus.Failed => HabitExecutionStatusData.Failed,
      ExecutionStatus.Skipped => HabitExecutionStatusData.Skipped,
      ExecutionStatus.Freeze => HabitExecutionStatusData.Frozen,
      ExecutionStatus.Vacation => HabitExecutionStatusData.Vacation,
      _ => HabitExecutionStatusData.Unknown
    };
    public static HabitExecutionPrincipal ToPrincipal(this HabitExecutionData data)
    {
      return new HabitExecutionPrincipal
      {
        Id = data.Id,
        HabitVersionId = data.HabitVersionId,
        Record = new HabitExecutionRecord
        {
          Date = data.Date,
          Status = data.Status.ToDomainStatus(),
          CompletedAt = data.CompletedAt,
          Notes = data.Notes
        }
      };
    }

    public static HabitExecutionData ToData(this HabitExecutionPrincipal principal)
    {
      return new HabitExecutionData
      {
        Id = principal.Id,
        HabitVersionId = principal.HabitVersionId,
        Date = principal.Record.Date,
        Status = principal.Record.Status.ToDataStatus(),
        CompletedAt = principal.Record.CompletedAt,
        Notes = principal.Record.Notes
      };
    }

    public static Domain.Habit.HabitExecution ToDomain(this HabitExecutionData data, HabitVersionPrincipal habitVersionPrincipal)
    {
      return new Domain.Habit.HabitExecution
      {
        Principal = data.ToPrincipal(),
        HabitVersion = habitVersionPrincipal
      };
    }
  }
}
