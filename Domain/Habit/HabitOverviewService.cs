using CSharp_Result;
using Domain.Allowance;
using Domain.Charity;
using Domain.Check;
using Domain.Configuration;
using Domain.Exceptions;
using Domain.NfcTag;
using Domain.Proof;
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
  ICheckRepository checkRepository,
  INfcTagRepository nfcTagRepository,
  IProofBindingRepository proofBindingRepository,
  ITransactionManager tm
) : IHabitOverviewService
{
  public const string NfcProof = "nfc";

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
        .ThenAwait(ctx5 => nfcTagRepository.ListTaggedHabits(search.UserId)
          .Then(tagged => new {
              ctx5.habits,
              ctx5.userTz,
              ctx5.debtsByHabit,
              ctx5.totalDebt,
              ctx5.usedSkip,
              ctx5.totalSkip,
              tagged
            }, Errors.MapNone)
        )
        .ThenAwait(ctx6 => proofBindingRepository.ListPluginKeys(search.UserId)
          .Then(bound => new {
              ctx6.habits,
              ctx6.userTz,
              ctx6.debtsByHabit,
              ctx6.totalDebt,
              ctx6.usedSkip,
              ctx6.totalSkip,
              ctx6.tagged,
              bound
            }, Errors.MapNone)
        )
        .ThenAwait(async ctx =>
        {
          // Everything per habit is read once for all habits, so the number of queries
          // doesn't grow with the habit count.
          var utcNow = nowUtc ?? DateTime.UtcNow;
          var today = StreakCalculator.TodayFor(ctx.userTz, utcNow);
          var habitIds = ctx.habits.Select(h => h.HabitId).Distinct().ToList();
          var charities = new Dictionary<Guid, CharityPrincipal>();
          return await habitRepository.GetHabits(habitIds)
            .ThenAwait(habits => streakService.GetStatusForHabits(ctx.userTz, ctx.habits, utcNow)
              .Then(statuses => new { habits = habits.ToDictionary(h => h.Id), statuses }, Errors.MapNone))
            .ThenAwait(b => checkRepository.ListForDate(habitIds, today)
              .Then(checks => new { b.habits, b.statuses, checks = checks.ToDictionary(c => c.HabitId) }, Errors.MapNone))
            .ThenAwait(async b =>
            {
              var acc = new List<HabitOverviewItem>().ToAsyncResult();
              foreach (var hv in ctx.habits.OrderByDescending(x => x.Version))
              {
                acc = acc
                  .ThenAwait(list => BuildItem(hv, search.UserId, ctx.userTz, today,
                    b.habits.GetValueOrDefault(hv.HabitId), b.statuses[hv.HabitId], b.checks.GetValueOrDefault(hv.HabitId),
                    ctx.debtsByHabit.TryGetValue(hv.HabitId, out var ds) ? ds : [],
                    // A bound plugin (strava) wins over an NFC tag; Check in is always available too.
                    ctx.bound.TryGetValue(hv.HabitId, out var plugin) ? plugin
                    : ctx.tagged.Contains(hv.HabitId) ? NfcProof : CheckService.ManualProof)
                    .Then(item => { list.Add(item); return list; }, Errors.MapNone));
              }
              return await acc
                .Then(items => new HabitOverviewSummary(items, ctx.totalDebt, ctx.usedSkip, ctx.totalSkip), Errors.MapNone);
            });

          // Habits usually share a charity, so each one is read once. A habit without a
          // money stake has none.
          Task<Result<CharityPrincipal?>> CharityFor(Guid? charityId) =>
            charityId is not { } id ? Task.FromResult<Result<CharityPrincipal?>>((CharityPrincipal?)null)
            : charities.TryGetValue(id, out var cached) ? Task.FromResult<Result<CharityPrincipal?>>(cached)
            : charityService.Get(id)
              .NullToError(id.ToString())
              .Then(ch => (CharityPrincipal?)(charities[id] = ch.Principal), Errors.MapNone);

          Task<Result<HabitOverviewItem>> BuildItem(
            HabitVersionPrincipal hv,
            string userId,
            string userTz,
            DateOnly localToday,
            HabitPrincipal? habitPrincipal,
            HabitStreakStatus status,
            CheckPrincipal? check,
            List<HabitDebtItem> debtsForHabit,
            string proof)
          {
            if (habitPrincipal == null || habitPrincipal.UserId != userId)
              return Task.FromResult<Result<HabitOverviewItem>>(
                new NotFoundException("Habit Not Found", typeof(HabitPrincipal), hv.HabitId.ToString()));

            return CharityFor(hv.Record.CharityId)
              .Then(charity => new HabitOverviewItem(
                  hv.HabitId,
                  hv.Record.Task,
                  hv.Record.NotificationTime.ToString("HH:mm"),
                  userTz,
                  hv.Record.DaysOfWeek,
                  hv.Record.Stake.Amount,
                  hv.Record.Stake.Currency.Code,
                  habitPrincipal.Record.Enabled,
                  charity,
                  status,
                  status.TimeLeftToEodMinutes,
                  new HabitVersionMeta(hv.Id, hv.Version, true),
                  debtsForHabit.Sum(d => d.Amount),
                  hv.Record.DeadlineTime,
                  TodayDeadline(hv, userTz, localToday, check),
                  proof,
                  habitPrincipal.Record.RoutineId
                ), Errors.MapNone);
          }
        })
    );

    // Today's check End: the stored check when it exists (it may have been re-timed),
    // otherwise the window the scheduler will open. Null on days the habit isn't scheduled.
    static DateTime? TodayDeadline(HabitVersionPrincipal hv, string userTz, DateOnly today, CheckPrincipal? check)
    {
      if (check != null) return check.Record.DeadlineUtc;
      var scheduled = hv.Record.DaysOfWeek.Any(d =>
        string.Equals(d, today.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));
      if (!scheduled) return null;
      return CheckWindow.For(today, CheckWindow.Resolve(userTz), hv.Record.DeadlineTime).DeadlineUtc;
    }
  }
}
