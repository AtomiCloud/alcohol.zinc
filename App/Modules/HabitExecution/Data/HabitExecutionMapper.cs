using Domain.Habit;

namespace App.Modules.HabitExecution.Data
{
  public static class HabitExecutionMapper
  {
    public static HabitExecutionPrincipal ToPrincipal(this HabitExecutionData data)
    {
      return new HabitExecutionPrincipal
      {
        Id = data.Id,
        HabitVersionId = data.HabitVersionId,
        Record = new HabitExecutionRecord
        {
          Date = data.Date,
          Status = data.Status,
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
        Status = principal.Record.Status,
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
