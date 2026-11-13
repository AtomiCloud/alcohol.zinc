namespace App.Modules.Habit.API.V1;

public record OverviewQuery(int? Limit, int? Skip);

public record HabitOverviewResponse(
  List<HabitOverviewHabitRes> Habits,
  string TotalDebt,
  int UsedSkip,
  int TotalSkip
);

public record HabitOverviewHabitRes(
  string Id,
  string Name,
  string NotificationTime,
  string Timezone,
  bool[] Days,
  StakeRes Stake,
  bool Enabled,
  HabitCharityRefRes? Charity,  // null for a habit without a money stake
  HabitStatusRes Status,
  int TimeLeftToEodMinutes,
  HabitVersionMetaRes Version,
  string TotalDebt,
  string DeadlineTime,            // "HH:mm" in the user's timezone
  DateTime? TodayDeadlineUtc,     // end of today's check (UTC); null when not scheduled today
  string Proof,                   // "manual" | "nfc" | a bound plugin key ("strava")
  string? RoutineId,
  int SortOrder                   // position in the user's habit list; habits come in this order
);

public record StakeRes(decimal Amount, string Currency);

public record HabitCharityRefRes(string Id, string Name, string? Url);

public record HabitStatusRes(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  WeekStatusRes Week,
  string Today,
  string TodayStatus
);

// For each day of the user's week, return one of:
// "failed", "succeeded", "vacation", "skip", "frozen", or "not_applicable"
public record WeekStatusRes(
  string Sunday,
  string Monday,
  string Tuesday,
  string Wednesday,
  string Thursday,
  string Friday,
  string Saturday,
  string Start,
  string End
);

public record HabitVersionMetaRes(string Id, ushort Version, bool IsActive);
