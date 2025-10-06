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
  List<HabitVersionMetaRes> Versions
);

public record StakeRes(decimal Amount, string Currency);

public record HabitCharityRefRes(string Id, string Name, string? Url);

public record HabitStatusRes(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  WeekRes Week
);

public record WeekRes(
  bool Sunday,
  bool Monday,
  bool Tuesday,
  bool Wednesday,
  bool Thursday,
  bool Friday,
  bool Saturday,
  string Start,
  string End
);

public record HabitVersionMetaRes(string Id, ushort Version, bool IsActive);
