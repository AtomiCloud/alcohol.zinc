using System.Globalization;
using Domain.Habit;

namespace App.Modules.Habit.API.V1;

public static class HabitOverviewMapper
{
  public static HabitOverviewResponse ToRes(this HabitOverviewSummary s)
    => new(
      s.Items.Select(ToRes).ToList(),
      s.TotalUserDebtAmount.ToString("F2", CultureInfo.InvariantCulture)
    );

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
      new HabitVersionMetaRes(i.Version.Id.ToString(), i.Version.Version, i.Version.IsActive),
      i.TotalDebtAmount.ToString("F2", CultureInfo.InvariantCulture)
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
      new WeekStatusRes(
        MapDay(s.WeekStatuses[DayOfWeek.Sunday]),
        MapDay(s.WeekStatuses[DayOfWeek.Monday]),
        MapDay(s.WeekStatuses[DayOfWeek.Tuesday]),
        MapDay(s.WeekStatuses[DayOfWeek.Wednesday]),
        MapDay(s.WeekStatuses[DayOfWeek.Thursday]),
        MapDay(s.WeekStatuses[DayOfWeek.Friday]),
        MapDay(s.WeekStatuses[DayOfWeek.Saturday]),
        s.WeekStart.ToString("yyyy-MM-dd"),
        s.WeekEnd.ToString("yyyy-MM-dd")
      )
    );
  }

  private static string MapDay(HabitDayStatus s) => s switch
  {
    HabitDayStatus.Succeeded => "succeeded",
    HabitDayStatus.Failed => "failed",
    HabitDayStatus.Vacation => "vacation",
    HabitDayStatus.Skip => "skip",
    HabitDayStatus.Frozen => "frozen",
    _ => "not_applicable"
  };
}
