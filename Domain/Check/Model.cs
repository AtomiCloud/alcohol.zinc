namespace Domain.Check;

// One check per scheduled habit per local date. Created ahead as Pending by the
// scheduler (or on demand), settled exactly once. Its settled outcome is stored
// as the HabitExecution it links to, so streaks/penalties keep reading executions.
public enum CheckStatus
{
  Pending,
  Done,
  Missed,
  Skipped,
  Frozen,
  Vacation
}

public record CheckRecord
{
  public required DateOnly LocalDate { get; init; }
  public required string Timezone { get; init; }
  public required DateTime OpensAtUtc { get; init; }
  public required DateTime DeadlineUtc { get; init; }
  public required CheckStatus Status { get; init; }
  public DateTime? SettledAtUtc { get; init; }
  public string? SettledBy { get; init; }
  public Guid? ExecutionId { get; init; }
}

public record CheckPrincipal
{
  public required Guid Id { get; init; }
  public required Guid HabitId { get; init; }
  public required CheckRecord Record { get; init; }
}

// What the scheduler needs to know about an enabled habit to open and close its checks.
// Timezone is the user's timezone (Configuration), falling back to the habit version's.
public record SchedulableHabit(
  Guid HabitId,
  string UserId,
  Guid VersionId,
  string[] DaysOfWeek,
  TimeOnly DeadlineTime,
  string Timezone)
{
  public bool IsScheduledOn(DateOnly date) =>
    DaysOfWeek.Any(d => string.Equals(d, date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));
}

public enum EvidenceVerdict
{
  Passed,
  Failed
}

// What a proof plugin reports. (HabitId, ProofKey, ExternalId) is the idempotency key.
public record Evidence
{
  public required string ProofKey { get; init; }
  public required string ExternalId { get; init; }
  public required EvidenceVerdict Verdict { get; init; }
  public required DateTime OccurredAtUtc { get; init; }
  public string? Data { get; init; }
  public string? Notes { get; init; }
}

public record SettleCheck
{
  public required Guid CheckId { get; init; }
  public required Guid HabitVersionId { get; init; }
  public required string UserId { get; init; }
  public required CheckStatus Status { get; init; }
  public required string SettledBy { get; init; }
  public required DateTime SettledAtUtc { get; init; }
  public DateTime? CompletedAtUtc { get; init; }
  public string? Notes { get; init; }
  public Evidence? Evidence { get; init; }

  // A user's skip: Settle counts the month's skips against this limit before writing one.
  public SkipQuota? SkipQuota { get; init; }
}

// The monthly skip limit and the user's local month it counts in.
public record SkipQuota(Entitlement.Quota Quota, DateOnly MonthStart, DateOnly MonthEnd);

// A check settled by the scheduler or by a timezone change (Missed, Frozen or Vacation).
public record SettledCheck(Guid HabitId, DateOnly LocalDate, CheckStatus Status);

// Outbox row written in the same transaction as the settle.
public record CheckEventPrincipal
{
  public required Guid Id { get; init; }
  public required Guid CheckId { get; init; }
  public required Guid HabitId { get; init; }
  public required string UserId { get; init; }
  public required CheckStatus Outcome { get; init; }
  public Guid? ExecutionId { get; init; }
  public required int Attempts { get; init; }
}
