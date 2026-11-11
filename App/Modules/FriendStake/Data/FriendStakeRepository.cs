using App.StartUp.Database;
using CSharp_Result;
using Domain.Friend;
using Domain.FriendStake;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.FriendStake.Data;

public class FriendStakeRepository(MainDbContext db, ILogger<FriendStakeRepository> logger)
  : IFriendStakeRepository
{
  // Only watchers still friends with the habit's owner: a stale row of a removed friend
  // never tells them anything.
  public async Task<Result<List<FriendUser>>> GetWatchers(Guid habitId)
  {
    try
    {
      return await (from w in db.HabitFriendWatchers
                    join h in db.Habits on w.HabitId equals h.Id
                    join f in db.Friendships
                      on new { h.UserId, w.FriendUserId } equals new { f.UserId, f.FriendUserId }
                    join u in db.Users on w.FriendUserId equals u.Id
                    where w.HabitId == habitId && h.DeletedAt == null
                    orderby u.Username, u.Id
                    select new FriendUser(u.Id, u.Username))
        .AsNoTracking()
        .ToListAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetWatchers failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<List<FriendUser>>> ReplaceWatchers(Guid habitId, IReadOnlyList<string> friendUserIds)
  {
    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
      // Lock the habit so concurrent replaces of the same habit serialize.
      await db.Database.ExecuteSqlAsync($@"SELECT 1 FROM ""Habits"" WHERE ""Id"" = {habitId} FOR UPDATE");
      await db.HabitFriendWatchers.Where(w => w.HabitId == habitId).ExecuteDeleteAsync();
      db.HabitFriendWatchers.AddRange(friendUserIds.Distinct()
        .Select(id => new HabitFriendWatcherData { HabitId = habitId, FriendUserId = id }));
      await db.SaveChangesAsync();
      await tx.CommitAsync();
      db.ChangeTracker.Clear();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ReplaceWatchers failed for HabitId={HabitId}", habitId);
      await tx.RollbackAsync();
      db.ChangeTracker.Clear();
      throw;
    }

    return await GetWatchers(habitId);
  }
}
