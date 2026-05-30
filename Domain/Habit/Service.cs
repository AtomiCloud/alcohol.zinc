using CSharp_Result;
using Domain.Entitlement;
using Domain.Penalty;
using Domain.Protection;
using Domain.Vacation;
using Microsoft.Extensions.Logging;
using NodaMoney;

namespace Domain.Habit;

public class HabitService(
  IHabitRepository repo,
  IVacationRepository vacationRepo,
  IProtectionRepository protectionRepo,
  IEntitlementService entitlementService,
  IPenaltyRepository penaltyRepo,
  ILogger<HabitService> logger
) : IHabitService
{
  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
  {
    return repo.SearchHabits(habitSearch);
  }

  public Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId)
    => repo.GetCurrentVersion(userId, habitId);

  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
  {
    return repo.Create(userId, versionRecord);
  }

  public async Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId,
    HabitVersionRecord versionRecord, bool enabled)
  {
    // First verify the habit belongs to the user
    var habitResult = await repo.GetHabit(habitId);
    HabitPrincipal? habit = habitResult;

    if (habit == null || habit.UserId != userId)
    {
      return (HabitVersionPrincipal?)null;
    }

    return await repo.Update(habitId, userId, versionRecord, enabled);
  }

  public Task<Result<Unit?>> Delete(Guid habitId, string userId)
  {
    return repo.Delete(habitId, userId);
  }

  public async Task<Result<int>> MarkDailyFailures(List<Guid> habitIds, DateOnly date)
  {
    // Phase 1: apply Vacation protections before failures
    // 1) Resolve scheduled habit versions for provided habitIds on the given date
    var activeVersionsResult = await repo.GetActiveHabitVersionsByIds(habitIds, date);
    var activeVersions = (List<HabitVersionPrincipal>)activeVersionsResult;

    // 2) Group by user (owners)
    var habitsResult = await repo.GetHabitsByIds(habitIds);
    var habits = (List<HabitPrincipal>)habitsResult;
    var ownerByHabit = habits.ToDictionary(h => h.Id, h => h.UserId);

    // Build user -> list of hvIds that are active today
    var hvByUser = new Dictionary<string, List<Guid>>();
    foreach (var hv in activeVersions)
    {
      if (!ownerByHabit.TryGetValue(hv.HabitId, out var uid)) continue;
      hvByUser.TryAdd(uid, []);
      hvByUser[uid].Add(hv.Id);
    }

    // 3) For users on vacation today, insert Vacation executions for their active versions
    var totalProtected = 0;
    foreach (var kv in hvByUser)
    {
      var userId = kv.Key;
      var hvIds = kv.Value;
      var vacations = await vacationRepo.ListActiveForUserOnDate(userId, date);
      var vs = (List<VacationPrincipal>)vacations;
      if (vs.Count > 0 && hvIds.Count > 0)
      {
        var inserted = await repo.CreateExecutionsForVersionsWithStatus(hvIds, date, ExecutionStatus.Vacation);
        totalProtected += (int)inserted;
      }
    }

    // 4) Freeze consumption: If user has freeze available and no completion/skip for any scheduled habit today,
    //    consume one user-level freeze day and mark all scheduled versions as Frozen.
    var totalFrozen = 0;
    foreach (var kv in hvByUser)
    {
      var userId = kv.Key;
      var hvIds = kv.Value;
      if (hvIds.Count == 0) continue;

      var anyDone = await repo.HasAnyCompletedOrSkippedForVersions(hvIds, date);
      if (anyDone)
        continue;

      var consumed = await protectionRepo.TryConsumeFreeze(userId, date);
      if ((bool)consumed)
      {
        var inserted = await repo.CreateExecutionsForVersionsWithStatus(hvIds, date, ExecutionStatus.Freeze);
        totalFrozen += (int)inserted;
      }
    }

    // 5) Fail remaining scheduled executions (LEFT JOIN prevents double-insert)
    var failedResult = await repo.CreateFailedExecutions(habitIds, date);
    var failedRows = (List<FailedExecutionRow>)failedResult;

    // 6) Enqueue a Pending penalty per failed row (idempotent via unique HabitExecutionId)
    foreach (var row in failedRows)
    {
      // Exact cents math (mirror StreakRepository): cents * bps / 10000
      var amountCents = (long)row.StakeCents * row.RatioBasisPoints / 10_000L;
      if (amountCents <= 0) continue; // skip zero/negative penalties

      var amount = new Money(amountCents / 100m, Currency.FromCode(row.StakeCurrency));
      var record = new PenaltyRecord
      {
        HabitExecutionId = row.ExecutionId,
        UserId = row.UserId,
        CharityId = row.CharityId,
        Amount = amount,
        Status = PenaltyStatus.Pending,
        PaymentIntentId = null,
        Attempts = 0,
        LastError = null
      };
      await penaltyRepo.EnqueuePending(record); // idempotent; no-op if execution already enqueued
    }

    return failedRows.Count + totalProtected + totalFrozen;
  }

  public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, string? notes)
  {
    return repo
      .GetUserCurrentDate(userId, habitVersionId)
      .ThenAwait(date => repo.CompleteHabit(userId, habitVersionId, date, notes));
  }

  public Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, string? notes)
  {
    return repo
      .GetUserCurrentDate(userId, habitVersionId)
      .ThenAwait(date => repo.SkipHabit(userId, habitVersionId, date, notes));
  }

  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId,
    HabitExecutionSearch habitExecutionSearch)
  {
    return repo.SearchHabitExecutions(userId, habitExecutionSearch);
  }

  public async Task<Result<int>> MarkDailyFailuresForTimezonesNearMidnight(DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    return await repo.GetDistinctTimezonesForEnabledHabits()
      .ThenAwait(async tzList =>
      {
        List<Result<int>> results = [];
        foreach (var tz in tzList)
        {
          logger.LogInformation("Processing Timezone: {tz}", tz);
          var r = await this.ProcessTimezoneForFailures(tz, now);
          results.Add(r);
        }

        var aggregated = results
          .ToResultOfSeq()
          .Then(xs => xs.Sum(), Errors.MapAll);
        return aggregated;
      });
  }

  private async Task<Result<int>> ProcessTimezoneForFailures(string tzId, DateTime nowUtc)
  {
    TimeZoneInfo tz;
    try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
    catch { return 0; }

    var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), tz);
    // After-midnight window: [00:00, 00:50). Deliberately wider than the 15-min
    // timer interval so a tick is guaranteed to land in-window even if one is
    // delayed/skipped. Overlapping passes within the window are safe: the daily
    // job's only side effect is EnqueuePending, which is idempotent (no-op on the
    // unique HabitExecutionId), so re-projecting already-Failed rows cannot
    // double-enqueue or abort the batch.
    var inWindow = localNow.TimeOfDay >= TimeSpan.Zero && localNow.TimeOfDay < TimeSpan.FromMinutes(50);
    logger.LogInformation("Timezone: {tz}, in window: {inWindow}", tz, inWindow);
    if (!inWindow) return 0;

    var dateToMark = DateOnly.FromDateTime(localNow.AddDays(-1));

    return await repo.GetEnabledHabitIdsByTimezone(tzId)
      .ThenAwait<List<Guid>, int>(async habitIds =>
      {
        if (habitIds.Count == 0) return 0;
        return await this.MarkDailyFailures(habitIds, dateToMark);
      });
  }
}
