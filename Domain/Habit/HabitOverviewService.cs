using CSharp_Result;
using Domain.Charity;
using Domain.Exceptions;

namespace Domain.Habit;

public class HabitOverviewService(
  IHabitService habitService,
  IHabitRepository habitRepository,
  Domain.Configuration.IConfigurationService configurationService,
  ICharityService charityService,
  IStreakService streakService,
  ITransactionManager tm
) : IHabitOverviewService
{
  public Task<Result<List<HabitOverviewItem>>> GetOverview(string userId, int limit, int skip, DateTime? nowUtc = null)
  {
    return tm.Start<List<HabitOverviewItem>>(async () =>
    {
      var habits = await habitService.SearchHabits(new HabitSearch
      {
        UserId = userId,
        Limit = limit,
        Skip = skip
      });
      if (!habits.IsSuccess()) return habits.FailureOrDefault();

      var items = new List<HabitOverviewItem>();
      // Resolve user's timezone from configuration (fallback to UTC if none)
      var configRes = await configurationService.GetByUserId(userId);
      var userTz = (configRes.IsSuccess() && configRes.Get() != null)
        ? configRes.Get()!.Principal.Record.Timezone
        : "UTC";
      foreach (var hv in habits.Get().OrderByDescending(x => x.Version))
      {
        // Habit enabled flag
        var habitPrincipalRes = await habitRepository.GetHabit(hv.HabitId);
        HabitPrincipal? habitPrincipal = habitPrincipalRes;
        if (habitPrincipal == null || habitPrincipal.UserId != userId)
          return new NotFoundException("Habit Not Found", typeof(HabitPrincipal), hv.HabitId.ToString());

        // Charity (aggregate -> principal)
        var charityAggRes = await charityService.Get(hv.Record.CharityId);
        Domain.Charity.Charity? charityAgg = charityAggRes;
        if (charityAgg == null) return new NotFoundException("Charity not found", typeof(CharityPrincipal), hv.Record.CharityId.ToString());
        var charity = charityAgg.Principal;

        // Streak (user timezone for today/week; habit timezone for streak series + EOD countdown)
        var streakRes = await streakService.GetStatusForHabit(userId, hv.HabitId, userTz, hv.Record.Timezone, hv.Record.DaysOfWeek, nowUtc);
        if (!streakRes.IsSuccess()) return streakRes.FailureOrDefault();

        // Versions (all for this habit) and active indicator
        var versionsRes = await habitRepository.GetVersions(userId, hv.HabitId);
        if (!versionsRes.IsSuccess()) return versionsRes.FailureOrDefault();
        var versionsList = versionsRes.Get()
          .Select(v => new HabitVersionMeta(v.Id, v.Version, v.Version == hv.Version))
          .OrderByDescending(v => v.Version)
          .ToList();

        items.Add(new HabitOverviewItem(
          hv.HabitId,
          hv.Record.Task,
          hv.Record.NotificationTime.ToString("HH:mm"),
          hv.Record.Timezone,
          hv.Record.DaysOfWeek,
          hv.Record.Stake.Amount,
          hv.Record.Stake.Currency.Code,
          habitPrincipal.Record.Enabled,
          charity,
          streakRes.Get(),
          streakRes.Get().TimeLeftToEodMinutes,
          versionsList
        ));
      }

      return items;
    });
  }
}
