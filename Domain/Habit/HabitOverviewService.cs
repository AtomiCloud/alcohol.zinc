using CSharp_Result;
using Domain.Allowance;
using Domain.Charity;
using Domain.Configuration;
using Domain.Exceptions;
using Domain.Subscription;

namespace Domain.Habit;

public class HabitOverviewService(
  IHabitService habitService,
  IHabitRepository habitRepository,
  IConfigurationService configurationService,
  ICharityService charityService,
  IStreakService streakService,
  IStreakRepository streakRepository,
  IAllowanceService allowanceService,
  ISubscriptionService subscriptionService,
  ITransactionManager tm
) : IHabitOverviewService
{
  public Task<Result<HabitOverviewSummary>> GetOverview(HabitOverviewSearch search, string skipsMonthlyKey, 
    DateTime? nowUtc = null)
  {
    return tm.Start<HabitOverviewSummary>(() =>
      habitService.SearchHabits(new HabitSearch { UserId = search.UserId, Limit = search.Limit, Skip = search.Skip })
        .ThenAwait(habits => configurationService.GetByUserId(search.UserId)
          .NullToError(search.UserId)
          .Then(cfg => new { habits, userTz = cfg.Principal.Record.Timezone }, Errors.MapNone)
        )
        .ThenAwait(ctx1 => streakRepository.GetOpenDebtsForUser(search.UserId)
          .Then(debts => new {
              ctx1.habits,
              ctx1.userTz,
              debtsByHabit = debts.GroupBy(d => d.HabitId).ToDictionary(g => g.Key, g => g.ToList()),
              totalDebt = debts.Sum(d => d.Amount)
            }, Errors.MapNone)
        )
        .ThenAwait(ctx2 => allowanceService.GetUserMonthWindow(search.UserId, nowUtc)
          .Then(window => new {
              ctx2.habits,
              ctx2.userTz,
              ctx2.debtsByHabit,
              ctx2.totalDebt,
              monthWindow = window
            }, Errors.MapNone)
        )
        .ThenAwait(ctx3 => habitRepository.CountUserSkipsForMonth(search.UserId, ctx3.monthWindow.MonthStart, ctx3.monthWindow.MonthEnd)
          .Then(usedSkip => new {
              ctx3.habits,
              ctx3.userTz,
              ctx3.debtsByHabit,
              ctx3.totalDebt,
              usedSkip
            }, Errors.MapNone)
        )
        .ThenAwait(ctx4 => subscriptionService.GetUserTier(search.UserId)
          .ThenAwait(tier => subscriptionService.GetLimitForTier(tier, skipsMonthlyKey)
            .Then(totalSkip => new {
                ctx4.habits,
                ctx4.userTz,
                ctx4.debtsByHabit,
                ctx4.totalDebt,
                ctx4.usedSkip,
                totalSkip
              }, Errors.MapNone)
          )
        )
        .ThenAwait(async ctx =>
        {
          var acc = new List<HabitOverviewItem>().ToAsyncResult();
          foreach (var hv in ctx.habits.OrderByDescending(x => x.Version))
          {
            acc = acc
              .ThenAwait(list => BuildItem(hv, search.UserId, ctx.userTz, nowUtc, ctx.debtsByHabit.TryGetValue(hv.HabitId, out var ds) ? ds : [], ctx.usedSkip, ctx.totalSkip)
                .Then(item => { list.Add(item); return list; }, Errors.MapNone));
          }
          return await acc
            .Then(items => new HabitOverviewSummary(items, ctx.totalDebt), Errors.MapNone);
        })
    );

    Task<Result<HabitOverviewItem>> BuildItem(
      HabitVersionPrincipal hv,
      string userId,
      string userTz,
      DateTime? nowUtc,
      List<HabitDebtItem> debtsForHabit,
      int usedSkip,
      int totalSkip)
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
        .Then(ctx2 => new HabitOverviewItem(
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
            new HabitVersionMeta(hv.Id, hv.Version, true),
            debtsForHabit.Sum(d => d.Amount),
            usedSkip,
            totalSkip
          ), Errors.MapNone);
    }
  }
}
