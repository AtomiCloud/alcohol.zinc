using App.StartUp.Database;
using CSharp_Result;
using Domain.Habit;
using Domain.Payment;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Habit.Data;

public class StreakRepository(MainDbContext db, ILogger<StreakRepository> logger) : IStreakRepository
{
  public async Task<Result<int>> GetCurrentStreak(Guid habitId, DateOnly today)
  {
    try
    {
      logger.LogInformation("GetCurrentStreak habitId={HabitId} today={Today}", habitId, today);

      var lastFailed = await (from he in db.HabitExecutions
                              join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                              where hv.HabitId == habitId && he.Date <= today && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Failed
                              select (DateOnly?)he.Date)
        .OrderByDescending(d => d)
        .FirstOrDefaultAsync();

      var since = lastFailed ?? DateOnly.MinValue;

      var current = await (from he in db.HabitExecutions
                           join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                           where hv.HabitId == habitId
                                 && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed
                                 && he.Date > since
                                 && he.Date <= today
                           select he)
        .CountAsync();

      return current;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCurrentStreak failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<int>> GetMaxStreak(Guid habitId)
  {
    try
    {
      logger.LogInformation("GetMaxStreak habitId={HabitId}", habitId);

      var events = await (from he in db.HabitExecutions
                          join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                          where hv.HabitId == habitId
                          orderby he.Date
                          select new { he.Date, he.Status })
        .AsNoTracking()
        .ToListAsync();

      int cur = 0, max = 0;
      foreach (var e in events)
      {
        if (e.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed)
        {
          cur++;
          if (cur > max) max = cur;
        }
        else if (e.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Failed)
        {
          cur = 0;
        }
      }
      return max;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetMaxStreak failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<bool>> IsCompleteOn(Guid habitId, DateOnly date)
  {
    try
    {
      var done = await (from he in db.HabitExecutions
                        join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                        where hv.HabitId == habitId && he.Date == date && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed
                        select he.Id)
        .AnyAsync();
      return done;
    }
    catch (Exception e)
    {
      logger.LogError(e, "IsCompleteOn failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<HashSet<DateOnly>>> GetCompletedInRange(Guid habitId, DateOnly start, DateOnly end)
  {
    try
    {
      var results = await (from he in db.HabitExecutions
                           join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                           where hv.HabitId == habitId
                                 && he.Date >= start
                                 && he.Date <= end
                                 && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed
                           select he.Date)
        .ToListAsync();
      return results.ToHashSet();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCompletedInRange failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<bool>> HasCompletionBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc)
  {
    try
    {
      var done = await (from he in db.HabitExecutions
                        join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                        where hv.HabitId == habitId
                              && he.CompletedAt != null
                              && he.CompletedAt >= startUtc
                              && he.CompletedAt <= endUtc
                              && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed
                        select he.Id)
        .AnyAsync();
      return done;
    }
    catch (Exception e)
    {
      logger.LogError(e, "HasCompletionBetweenUtc failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<List<DateTime>>> GetCompletionsBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc)
  {
    try
    {
      var items = await (from he in db.HabitExecutions
                         join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                        where hv.HabitId == habitId
                               && he.CompletedAt != null
                               && he.CompletedAt >= startUtc
                               && he.CompletedAt <= endUtc
                               && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed
                         select he.CompletedAt!.Value)
        .ToListAsync();
      return items;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCompletionsBetweenUtc failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<List<Domain.Habit.HabitExecutionRecord>>> GetExecutionsInHabitDateRange(Guid habitId, DateOnly start, DateOnly end)
  {
    try
    {
      var items = await (from he in db.HabitExecutions
                         join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                         where hv.HabitId == habitId
                               && he.Date >= start
                               && he.Date <= end
                         select new { he.Date, he.Status, he.CompletedAt })
        .AsNoTracking()
        .ToListAsync();

      var results = new List<Domain.Habit.HabitExecutionRecord>();
      foreach (var it in items)
      {
        var status = it.Status switch
        {
          App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed => Domain.Habit.ExecutionStatus.Completed,
          App.Modules.HabitExecution.Data.HabitExecutionStatusData.Failed => Domain.Habit.ExecutionStatus.Failed,
          App.Modules.HabitExecution.Data.HabitExecutionStatusData.Skipped => Domain.Habit.ExecutionStatus.Skipped,
          App.Modules.HabitExecution.Data.HabitExecutionStatusData.Frozen => Domain.Habit.ExecutionStatus.Freeze,
          App.Modules.HabitExecution.Data.HabitExecutionStatusData.Vacation => Domain.Habit.ExecutionStatus.Vacation,
          _ => Domain.Habit.ExecutionStatus.Failed
        };

        results.Add(new Domain.Habit.HabitExecutionRecord
        {
          Date = it.Date,
          Status = status,
          CompletedAt = it.CompletedAt,
          Notes = null
        });
      }

      return results;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetExecutionsInHabitDateRange failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<List<Domain.Habit.HabitDebtItem>>> GetOpenDebtsForUser(string userId)
  {
    try
    {
      var rows = await (from he in db.HabitExecutions
                        join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                        join h in db.Habits on hv.HabitId equals h.Id
                        where h.UserId == userId
                              && he.Status == HabitExecution.Data.HabitExecutionStatusData.Failed
                              && !db.PaymentIntentExecutions
                                    .Join(db.PaymentIntents,
                                          pie => pie.PaymentIntentId,
                                          pi => pi.Id,
                                          (pie, pi) => new { pie.HabitExecutionId, pi.Status })
                                    .Any(x => x.HabitExecutionId == he.Id && x.Status == PaymentIntentStatus.Succeeded)
                        select new
                        {
                          he.Id,
                          he.Date,
                          hv.HabitId,
                          HabitVersionId = hv.Id,
                          hv.StakeCents,
                          hv.RatioBasisPoints,
                          hv.StakeCurrency,
                          hv.CharityId,
                          hv.Task
                        })
        .AsNoTracking()
        .ToListAsync();

      var items = rows.Select(r =>
      {
        var amountCents = (long)r.StakeCents * r.RatioBasisPoints / 10_000L;
        var amount = amountCents / 100m;
        return new HabitDebtItem(
          r.Id,
          r.HabitId,
          r.HabitVersionId,
          r.Date,
          amount,
          r.StakeCurrency,
          r.CharityId,
          r.Task
        );
      }).ToList();

      return items;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetOpenDebtsForUser failed for UserId={UserId}", userId);
      throw;
    }
  }
}
