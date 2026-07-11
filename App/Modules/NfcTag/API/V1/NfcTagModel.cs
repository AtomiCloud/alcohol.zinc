using App.Modules.Habit.API.V1;

namespace App.Modules.NfcTag.API.V1;

public record LinkNfcTagReq(
  Guid HabitId
);

public record NfcTagRes(
  string Id,
  string UserId,
  Guid HabitId,
  string ClaimedAt
);

public record NfcTagResolutionRes(
  NfcTagRes Tag,
  HabitVersionRes HabitVersion,     // current version — complete against this
  string Today,                      // today in the habit's timezone
  HabitExecutionRes? TodayExecution  // null = not yet acted on today
);
