using Domain.Habit;

namespace App.Modules.Habit.API.V1;

public static class HabitOverviewMapper
{
  public static HabitOverviewResponse ToRes(this List<HabitOverviewItem> items)
    => new(items.Select(ToRes).ToList());

  private static HabitOverviewHabitRes ToRes(HabitOverviewItem i)
  {
    return new HabitOverviewHabitRes(
      i.HabitId.ToString(),
      i.Name,
      i.NotificationTime,
      i.Timezone,
      ToDays(i.DaysOfWeek),
      new StakeRes(i.StakeAmount, i.StakeCurrency),
      i.Enabled,
      new HabitCharityRefRes(i.Charity.Id.ToString(), i.Charity.Record.Name, null),
      ToStatus(i.Status),
      i.TimeLeftToEodMinutes,
      i.Versions.Select(v => new HabitVersionMetaRes(v.Id.ToString(), v.Version, v.IsActive)).ToList()
    );
  }

  private static bool[] ToDays(string[] daysOfWeek)
  {
    var set = new HashSet<string>(daysOfWeek.Select(x => x.ToLowerInvariant()));
    return
    [
      set.Contains("sunday"),
      set.Contains("monday"),
      set.Contains("tuesday"),
      set.Contains("wednesday"),
      set.Contains("thursday"),
      set.Contains("friday"),
      set.Contains("saturday"),
    ];
  }

  private static HabitStatusRes ToStatus(HabitStreakStatus s)
  {
    return new HabitStatusRes(
      s.CurrentStreak,
      s.MaxStreak,
      s.IsCompleteToday,
      new WeekRes(
        s.Week[DayOfWeek.Sunday],
        s.Week[DayOfWeek.Monday],
        s.Week[DayOfWeek.Tuesday],
        s.Week[DayOfWeek.Wednesday],
        s.Week[DayOfWeek.Thursday],
        s.Week[DayOfWeek.Friday],
        s.Week[DayOfWeek.Saturday],
        s.WeekStart.ToString("yyyy-MM-dd"),
        s.WeekEnd.ToString("yyyy-MM-dd")
      )
    );
  }
}
