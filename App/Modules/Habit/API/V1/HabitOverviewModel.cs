namespace App.Modules.Habit.API.V1;

public record OverviewQuery(int? Limit, int? Skip);

public record HabitOverviewResponse(List<HabitOverviewHabitRes> Habits);

public record HabitOverviewHabitRes(
  string Id,
  string Name,
  string NotificationTime,
  string Timezone,
  bool[] Days,
  StakeRes Stake,
  bool Enabled,
  HabitCharityRefRes Charity,
  HabitStatusRes Status,
  int TimeLeftToEodMinutes,
  HabitVersionMetaRes Version
);

public record StakeRes(decimal Amount, string Currency);

public record HabitCharityRefRes(string Id, string Name, string? Url);

public record HabitStatusRes(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  WeekStatusRes Week
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
