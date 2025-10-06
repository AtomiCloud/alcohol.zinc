using App.StartUp.Database;
using CSharp_Result;
using Domain.Habit;
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
                              where hv.HabitId == habitId && he.Date <= today && he.Status == ExecutionStatus.Failed
                              select (DateOnly?)he.Date)
        .OrderByDescending(d => d)
        .FirstOrDefaultAsync();

      var since = lastFailed ?? DateOnly.MinValue;

      var current = await (from he in db.HabitExecutions
                           join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                           where hv.HabitId == habitId
                                 && he.Status == ExecutionStatus.Completed
                                 && he.Date > since
                                 && he.Date <= today
                           select he)
        .CountAsync();

      return current;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCurrentStreak failed for HabitId={HabitId}", habitId);
      return e;
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
        if (e.Status == ExecutionStatus.Completed)
        {
          cur++;
          if (cur > max) max = cur;
        }
        else if (e.Status == ExecutionStatus.Failed)
        {
          cur = 0;
        }
      }
      return max;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetMaxStreak failed for HabitId={HabitId}", habitId);
      return e;
    }
  }

  public async Task<Result<bool>> IsCompleteOn(Guid habitId, DateOnly date)
  {
    try
    {
      var done = await (from he in db.HabitExecutions
                        join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                        where hv.HabitId == habitId && he.Date == date && he.Status == ExecutionStatus.Completed
                        select he.Id)
        .AnyAsync();
      return done;
    }
    catch (Exception e)
    {
      logger.LogError(e, "IsCompleteOn failed for HabitId={HabitId}", habitId);
      return e;
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
                                 && he.Status == ExecutionStatus.Completed
                           select he.Date)
        .ToListAsync();
      return results.ToHashSet();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCompletedInRange failed for HabitId={HabitId}", habitId);
      return e;
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
                              && he.Status == ExecutionStatus.Completed
                        select he.Id)
        .AnyAsync();
      return done;
    }
    catch (Exception e)
    {
      logger.LogError(e, "HasCompletionBetweenUtc failed for HabitId={HabitId}", habitId);
      return e;
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
                               && he.Status == ExecutionStatus.Completed
                         select he.CompletedAt!.Value)
        .ToListAsync();
      return items;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetCompletionsBetweenUtc failed for HabitId={HabitId}", habitId);
      return e;
    }
  }
}
