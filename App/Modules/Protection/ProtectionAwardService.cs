using CSharp_Result;
using Domain.Configuration;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace App.Modules.Protection;

// Extracted from WeeklyFreezeAwardHostedService: event-driven per-user evaluation
public class ProtectionAwardService(
  IServiceProvider provider,
  ILogger<ProtectionAwardService> logger
) : IProtectionAwardService
{
  public async Task<Result<int>> AwardWeeklyFreezesForUser(string userId, DateTime? nowUtc = null)
  {
    try
    {
      using var scope = provider.CreateScope();
      var habitRepo = scope.ServiceProvider.GetRequiredService<IHabitRepository>();
      var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
      var streakService = scope.ServiceProvider.GetRequiredService<IStreakService>();
      var streakRepo = scope.ServiceProvider.GetRequiredService<IStreakRepository>();
      var protectionRepo = scope.ServiceProvider.GetRequiredService<IProtectionRepository>();
      var entitlement = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

      // Fetch user's timezone
      var cfgRes = await configService.GetByUserId(userId);
      if (!cfgRes.IsSuccess()) return cfgRes.FailureOrDefault()!;
      var cfg = (Domain.Configuration.Configuration?)cfgRes;
      if (cfg?.Principal?.Record == null) return 0; // user config missing -> nothing to award
      var userTzId = cfg.Principal.Record.Timezone;
      var userTz = TimeZoneInfo.FindSystemTimeZoneById(userTzId);

      // Determine the last completed week (previous Sunday–Saturday in user-local time)
      var userToday = StreakCalculator.TodayFor(userTzId, nowUtc);
      var previous = userToday.AddDays(-1);
      var (weekStart, weekEnd) = StreakCalculator.WeekSundayBounds(previous);

      // Compute UTC pivot inside that week
      var localMid = new DateTime(weekEnd.Year, weekEnd.Month, weekEnd.Day, 12, 0, 0, DateTimeKind.Unspecified);
      var pivotUtc = TimeZoneInfo.ConvertTimeToUtc(localMid, userTz);

      // Get all enabled habits for this user (paged)
      const int pageSize = 200;
      var skip = 0;
      var totalAwards = 0;

      while (true)
      {
        var pageRes = await habitRepo.SearchHabits(new HabitSearch
        {
          Id = null,
          UserId = userId,
          Task = null,
          Enabled = true,
          Limit = pageSize,
          Skip = skip
        });
        if (!pageRes.IsSuccess()) return pageRes.FailureOrDefault()!;

        var versions = (List<HabitVersionPrincipal>)pageRes;
        if (versions.Count == 0) break;

        foreach (var v in versions)
        {
          var daysOfWeek = v.Record.DaysOfWeek ?? [];
          var habitTz = v.Record.Timezone ?? userTzId;
          var statusRes = await streakService.GetStatusForHabit(userId, v.HabitId, userTzId, habitTz, daysOfWeek, pivotUtc);
          if (!statusRes.IsSuccess())
          {
            logger.LogWarning(statusRes.FailureOrDefault(), "AwardWeekly: GetStatusForHabit failed for HabitId={HabitId}", v.HabitId);
            continue;
          }

          var status = (HabitStreakStatus)statusRes;
          // perfect = all scheduled days succeeded
          var scheduled = new HashSet<string>((daysOfWeek ?? []).Select(x => x.ToLowerInvariant()));
          var perfect = true;
          foreach (var dow in Enum.GetValues<DayOfWeek>())
          {
            var key = dow.ToString().ToLowerInvariant();
            if (!scheduled.Contains(key)) continue;
            if (!status.WeekStatuses.TryGetValue(dow, out var val) || val != HabitDayStatus.Succeeded)
            { perfect = false; break; }
          }
          if (!perfect) continue;

          // Idempotent award per habit/week
          var recorded = await protectionRepo.RecordFreezeAwardIfAbsent(v.HabitId, weekStart);
          if (!recorded.IsSuccess() || !(bool)recorded) continue;

          // Increment + clamp at user cap
          var incRes = await protectionRepo.IncrementFreeze(userId, 1);
          if (!incRes.IsSuccess())
          {
            logger.LogWarning(incRes.FailureOrDefault(), "AwardWeekly: IncrementFreeze failed for UserId={UserId}", userId);
            continue;
          }

          var maxStreakRes = await streakRepo.GetUserMaxStreakAcrossHabits(userId);
          if (!maxStreakRes.IsSuccess())
          {
            logger.LogWarning(maxStreakRes.FailureOrDefault(), "AwardWeekly: GetUserMaxStreakAcrossHabits failed for UserId={UserId}", userId);
            continue;
          }
          var capRes = await entitlement.GetFreezeCapForUser(userId, (int)maxStreakRes);
          if (!capRes.IsSuccess())
          {
            logger.LogWarning(capRes.FailureOrDefault(), "AwardWeekly: GetFreezeCapForUser failed for UserId={UserId}", userId);
            continue;
          }
          var clamp = await protectionRepo.ClampFreezeToCap(userId, (int)capRes);
          if (!clamp.IsSuccess())
          {
            logger.LogWarning(clamp.FailureOrDefault(), "AwardWeekly: ClampFreezeToCap failed for UserId={UserId}", userId);
          }
          else
          {
            totalAwards += 1;
          }
        }

        skip += versions.Count;
        if (versions.Count < pageSize) break;
      }

      return totalAwards;
    }
    catch (Exception e)
    {
      logger.LogError(e, "AwardWeeklyFreezesForUser failed");
      throw;
    }
  }
}

