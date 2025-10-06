using CSharp_Result;
using Domain.Charity;
using Domain.Configuration;
using Domain.Exceptions;

namespace Domain.Habit;

public class HabitOverviewService(
  IHabitService habitService,
  IHabitRepository habitRepository,
  IConfigurationService configurationService,
  ICharityService charityService,
  IStreakService streakService,
  ITransactionManager tm
) : IHabitOverviewService
{
  public Task<Result<List<HabitOverviewItem>>> GetOverview(HabitOverviewSearch search, DateTime? nowUtc = null)
  {
    return tm.Start<List<HabitOverviewItem>>(() =>
      habitService.SearchHabits(new HabitSearch { UserId = search.UserId, Limit = search.Limit, Skip = search.Skip })
        .ThenAwait(habits => configurationService.GetByUserId(search.UserId)
          .NullToError(search.UserId)
          .Then(cfg => new { habits, userTz = cfg.Principal.Record.Timezone }, Errors.MapNone)
        )
        .ThenAwait(async ctx =>
        {
          var acc = new List<HabitOverviewItem>().ToAsyncResult();
          foreach (var hv in ctx.habits.OrderByDescending(x => x.Version))
          {
            acc = acc
              .ThenAwait(list => BuildItem(hv, search.UserId, ctx.userTz, nowUtc)
                .Then(item => { list.Add(item); return list; }, Errors.MapNone));
          }
          return await acc;
        })
    );

    Task<Result<HabitOverviewItem>> BuildItem(
      HabitVersionPrincipal hv,
      string userId,
      string userTz,
      DateTime? nowUtc)
    {
      return habitRepository.GetHabit(hv.HabitId)
        .NullToError(hv.HabitId.ToString())
        .Then(habitPrincipal =>
          habitPrincipal.UserId != userId
            ? new NotFoundException("Habit Not Found", typeof(HabitPrincipal), hv.HabitId.ToString())
            : habitPrincipal.ToResult()
        )
        .ThenAwait(habitPrincipal => charityService.Get(hv.Record.CharityId)
          .NullToError(hv.Record.CharityId.ToString())
          .Then(ch => ch.Principal, Errors.MapNone)
          .Then(charity => new { habitPrincipal, charity }, Errors.MapNone)
        )
        .ThenAwait(ctx => streakService.GetStatusForHabit(userId, hv.HabitId, userTz, hv.Record.Timezone, hv.Record.DaysOfWeek, nowUtc)
          .Then(status => new { ctx.habitPrincipal, ctx.charity, status }, Errors.MapNone)
        )
        .Then( ctx2 => new HabitOverviewItem(
            hv.HabitId,
            hv.Record.Task,
            hv.Record.NotificationTime.ToString("HH:mm"),
            hv.Record.Timezone,
            hv.Record.DaysOfWeek,
            hv.Record.Stake.Amount,
            hv.Record.Stake.Currency.Code,
            ctx2.habitPrincipal.Record.Enabled,
            ctx2.charity,
            ctx2.status,
            ctx2.status.TimeLeftToEodMinutes,
            new HabitVersionMeta(hv.Id, hv.Version, true)
          ), Errors.MapNone);
    }
  }
}
