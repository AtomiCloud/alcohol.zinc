using App.Modules.HabitExecution.Data;
using Domain.Check;

namespace App.Modules.Check.Data;

public static class CheckMapper
{
  public static CheckStatus ToDomain(this HabitCheckStatusData s) => s switch
  {
    HabitCheckStatusData.Done => CheckStatus.Done,
    HabitCheckStatusData.Missed => CheckStatus.Missed,
    HabitCheckStatusData.Skipped => CheckStatus.Skipped,
    HabitCheckStatusData.Frozen => CheckStatus.Frozen,
    HabitCheckStatusData.Vacation => CheckStatus.Vacation,
    _ => CheckStatus.Pending
  };

  public static HabitCheckStatusData ToData(this CheckStatus s) => s switch
  {
    CheckStatus.Done => HabitCheckStatusData.Done,
    CheckStatus.Missed => HabitCheckStatusData.Missed,
    CheckStatus.Skipped => HabitCheckStatusData.Skipped,
    CheckStatus.Frozen => HabitCheckStatusData.Frozen,
    CheckStatus.Vacation => HabitCheckStatusData.Vacation,
    _ => HabitCheckStatusData.Pending
  };

  public static HabitExecutionStatusData ToExecutionStatus(this CheckStatus s) => s switch
  {
    CheckStatus.Done => HabitExecutionStatusData.Completed,
    CheckStatus.Missed => HabitExecutionStatusData.Failed,
    CheckStatus.Skipped => HabitExecutionStatusData.Skipped,
    CheckStatus.Frozen => HabitExecutionStatusData.Frozen,
    CheckStatus.Vacation => HabitExecutionStatusData.Vacation,
    _ => HabitExecutionStatusData.Unknown
  };

  public static CheckStatus ToCheckStatus(this HabitExecutionStatusData s) => s switch
  {
    HabitExecutionStatusData.Completed => CheckStatus.Done,
    HabitExecutionStatusData.Skipped => CheckStatus.Skipped,
    HabitExecutionStatusData.Frozen => CheckStatus.Frozen,
    HabitExecutionStatusData.Vacation => CheckStatus.Vacation,
    _ => CheckStatus.Missed
  };

  public static CheckRecord ToRecord(this HabitCheckData data) => new()
  {
    LocalDate = data.LocalDate,
    Timezone = data.Timezone,
    OpensAtUtc = data.OpensAtUtc,
    DeadlineUtc = data.DeadlineUtc,
    Status = data.Status.ToDomain(),
    SettledAtUtc = data.SettledAtUtc,
    SettledBy = data.SettledBy,
    ExecutionId = data.ExecutionId
  };

  public static CheckPrincipal ToPrincipal(this HabitCheckData data) => new()
  {
    Id = data.Id,
    HabitId = data.HabitId,
    Record = data.ToRecord()
  };

  public static CheckEventPrincipal ToPrincipal(this CheckEventData data) => new()
  {
    Id = data.Id,
    CheckId = data.CheckId,
    HabitId = data.HabitId,
    UserId = data.UserId,
    Outcome = data.Outcome.ToDomain(),
    ExecutionId = data.ExecutionId,
    Attempts = data.Attempts
  };
}
