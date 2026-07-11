using Domain.Habit;

namespace Domain.NfcTag;

public record NfcTagRecord
{
  public required Guid HabitId { get; init; }
}

public record NfcTagPrincipal
{
  public required string Id { get; init; }  // tag id embedded in the physical tag's URL
  public required string UserId { get; init; }
  public required DateTime ClaimedAt { get; init; }
  public required NfcTagRecord Record { get; init; }
}

// Resolve aggregate: everything the client needs to act on a tap without
// storing version ids on the tag — the mapping, the habit's current version,
// and whether the habit was already acted on today (in the habit's timezone).
public record NfcTagResolution
{
  public required NfcTagPrincipal Principal { get; init; }
  public required HabitVersionPrincipal HabitVersion { get; init; }
  public required DateOnly Today { get; init; }
  public HabitExecutionPrincipal? TodayExecution { get; init; }
}
