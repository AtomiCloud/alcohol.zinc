using App.StartUp.Database;
using CSharp_Result;
using Domain.Friend;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Friend.Data;

public class FriendRepository(MainDbContext db, ILogger<FriendRepository> logger) : IFriendRepository
{
  public async Task<Result<FriendUser?>> GetUser(string userId)
  {
    try
    {
      return await db.Users.AsNoTracking()
        .Where(x => x.Id == userId)
        .Select(x => new FriendUser(x.Id, x.Username))
        .FirstOrDefaultAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetUser failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<List<FriendCandidate>>> Search(string userId, string query, int limit)
  {
    try
    {
      // Prefix match: the query's own %, _ and \ are escaped so they match literally.
      var pattern = EscapeLike(query) + "%";
      var users = await db.Users.AsNoTracking()
        .Where(x => x.Id != userId && EF.Functions.ILike(x.Username, pattern, @"\"))
        // lower(): the column's collation sorts all uppercase before lowercase.
        .OrderBy(x => x.Username.ToLower()).ThenBy(x => x.Username).ThenBy(x => x.Id)
        .Take(limit)
        .Select(x => new FriendUser(x.Id, x.Username))
        .ToListAsync();

      var ids = users.Select(x => x.UserId).ToList();
      var friends = (await db.Friendships.AsNoTracking()
        .Where(x => x.UserId == userId && ids.Contains(x.FriendUserId))
        .Select(x => x.FriendUserId).ToListAsync()).ToHashSet();
      var incoming = (await db.FriendRequests.AsNoTracking()
        .Where(x => x.ToUserId == userId && ids.Contains(x.FromUserId))
        .Select(x => x.FromUserId).ToListAsync()).ToHashSet();
      var outgoing = (await db.FriendRequests.AsNoTracking()
        .Where(x => x.FromUserId == userId && ids.Contains(x.ToUserId))
        .Select(x => x.ToUserId).ToListAsync()).ToHashSet();

      return users
        .Select(u => new FriendCandidate(u,
          RelationOf(friends.Contains(u.UserId), incoming.Contains(u.UserId), outgoing.Contains(u.UserId))))
        .ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Search friends failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<FriendRelation>> Relation(string userId, string otherUserId)
  {
    try
    {
      var friend = await db.Friendships.AsNoTracking()
        .AnyAsync(x => x.UserId == userId && x.FriendUserId == otherUserId);
      var incoming = await db.FriendRequests.AsNoTracking()
        .AnyAsync(x => x.FromUserId == otherUserId && x.ToUserId == userId);
      var outgoing = await db.FriendRequests.AsNoTracking()
        .AnyAsync(x => x.FromUserId == userId && x.ToUserId == otherUserId);
      return RelationOf(friend, incoming, outgoing);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Relation failed for UserId={UserId} OtherUserId={OtherUserId}", userId, otherUserId);
      throw;
    }
  }

  public async Task<Result<Unit>> CreateRequest(string fromUserId, string toUserId)
  {
    FriendRequestData? data = null;
    try
    {
      data = new FriendRequestData { FromUserId = fromUserId, ToUserId = toUserId };
      db.FriendRequests.Add(data);
      await db.SaveChangesAsync();
      return new Unit();
    }
    catch (UniqueConstraintException)
    {
      // A concurrent send of the same request won: it is already there.
      logger.LogInformation("Friend request FromUserId={FromUserId} ToUserId={ToUserId} already exists",
        fromUserId, toUserId);
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "CreateRequest failed for FromUserId={FromUserId} ToUserId={ToUserId}", fromUserId,
        toUserId);
      throw;
    }
    finally
    {
      // Never leave the row tracked: a failed Added entity would be re-inserted by the next SaveChanges.
      if (data != null) db.Entry(data).State = EntityState.Detached;
    }
  }

  public async Task<Result<FriendRequests>> ListRequests(string userId)
  {
    try
    {
      var incoming = await RequestPrincipals(db.FriendRequests.Where(x => x.ToUserId == userId));
      var outgoing = await RequestPrincipals(db.FriendRequests.Where(x => x.FromUserId == userId));
      return new FriendRequests(incoming, outgoing);
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListRequests failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<bool>> DeleteRequest(string userId, string otherUserId)
  {
    try
    {
      var affected = await db.FriendRequests
        .Where(x => (x.FromUserId == userId && x.ToUserId == otherUserId) ||
                    (x.FromUserId == otherUserId && x.ToUserId == userId))
        .ExecuteDeleteAsync();
      return affected > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "DeleteRequest failed for UserId={UserId} OtherUserId={OtherUserId}", userId, otherUserId);
      throw;
    }
  }

  public async Task<Result<FriendshipPrincipal>> CreateFriendship(string userId, string friendUserId)
  {
    try
    {
      try
      {
        return await CreateFriendshipOnce(userId, friendUserId);
      }
      catch (UniqueConstraintException)
      {
        // A concurrent accept of the same pair committed its rows first; this pass sees them
        // and only clears any requests left over.
        logger.LogInformation("Friendship UserId={UserId} FriendUserId={FriendUserId} created concurrently",
          userId, friendUserId);
        return await CreateFriendshipOnce(userId, friendUserId);
      }
    }
    catch (Exception e)
    {
      logger.LogError(e, "CreateFriendship failed for UserId={UserId} FriendUserId={FriendUserId}", userId,
        friendUserId);
      throw;
    }
  }

  private async Task<FriendshipPrincipal> CreateFriendshipOnce(string userId, string friendUserId)
  {
    var added = new List<FriendshipData>();
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var existing = await db.Friendships.AsNoTracking()
        .Where(x => (x.UserId == userId && x.FriendUserId == friendUserId) ||
                    (x.UserId == friendUserId && x.FriendUserId == userId))
        .Select(x => x.UserId)
        .ToListAsync();

      var now = DateTime.UtcNow;
      if (!existing.Contains(userId))
        added.Add(new FriendshipData { UserId = userId, FriendUserId = friendUserId, CreatedAtUtc = now });
      if (!existing.Contains(friendUserId))
        added.Add(new FriendshipData { UserId = friendUserId, FriendUserId = userId, CreatedAtUtc = now });
      db.Friendships.AddRange(added);
      await db.SaveChangesAsync();

      await db.FriendRequests
        .Where(x => (x.FromUserId == userId && x.ToUserId == friendUserId) ||
                    (x.FromUserId == friendUserId && x.ToUserId == userId))
        .ExecuteDeleteAsync();
      await tx.CommitAsync();
    }
    finally
    {
      foreach (var data in added) db.Entry(data).State = EntityState.Detached;
    }

    return await db.Friendships.AsNoTracking()
      .Where(x => x.UserId == userId && x.FriendUserId == friendUserId)
      .Select(x => new FriendshipPrincipal(new FriendUser(x.FriendUserId, x.FriendUser!.Username), x.CreatedAtUtc))
      .SingleAsync();
  }

  public async Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId)
  {
    try
    {
      return await db.Friendships.AsNoTracking()
        .Where(x => x.UserId == userId)
        .OrderBy(x => x.FriendUser!.Username.ToLower()).ThenBy(x => x.FriendUserId)
        .Select(x => new FriendshipPrincipal(new FriendUser(x.FriendUserId, x.FriendUser!.Username), x.CreatedAtUtc))
        .ToListAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListFriends failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<bool>> DeleteFriendship(string userId, string friendUserId)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var removed = await db.Friendships
        .Where(x => (x.UserId == userId && x.FriendUserId == friendUserId) ||
                    (x.UserId == friendUserId && x.FriendUserId == userId))
        .ExecuteDeleteAsync();

      // Each stops watching the other's habits (Habit.UserId is the owner).
      var userHabits = db.Habits.Where(h => h.UserId == userId).Select(h => h.Id);
      var friendHabits = db.Habits.Where(h => h.UserId == friendUserId).Select(h => h.Id);
      var watchers = await db.HabitFriendWatchers
        .Where(x => (x.FriendUserId == friendUserId && userHabits.Contains(x.HabitId)) ||
                    (x.FriendUserId == userId && friendHabits.Contains(x.HabitId)))
        .ExecuteDeleteAsync();
      await tx.CommitAsync();

      logger.LogInformation(
        "Deleted friendship UserId={UserId} FriendUserId={FriendUserId}: {Rows} rows, {Watchers} watchers",
        userId, friendUserId, removed, watchers);
      return removed > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "DeleteFriendship failed for UserId={UserId} FriendUserId={FriendUserId}", userId,
        friendUserId);
      throw;
    }
  }

  public async Task<Result<FriendInvitePrincipal>> GetOrCreateInvite(string userId, string code)
  {
    FriendInviteData? data = null;
    try
    {
      if (await InviteOf(userId) is { } existing) return existing;
      data = new FriendInviteData { Code = code, UserId = userId };
      db.FriendInvites.Add(data);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (UniqueConstraintException e)
    {
      // A concurrent first call created the user's invite: everyone gets that one code.
      if (await InviteOf(userId) is { } winner) return winner;
      logger.LogError(e, "GetOrCreateInvite code collision for UserId={UserId}", userId);
      throw;
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetOrCreateInvite failed for UserId={UserId}", userId);
      throw;
    }
    finally
    {
      if (data != null) db.Entry(data).State = EntityState.Detached;
    }
  }

  private async Task<FriendInvitePrincipal?> InviteOf(string userId) =>
    (await db.FriendInvites.AsNoTracking().Where(x => x.UserId == userId).FirstOrDefaultAsync())?.ToPrincipal();

  public async Task<Result<FriendInvitePrincipal?>> GetInvite(string code)
  {
    try
    {
      var data = await db.FriendInvites.AsNoTracking().Where(x => x.Code == code).FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetInvite failed");
      throw;
    }
  }

  // A friendship wins over requests; an incoming request wins over an outgoing one (when both
  // exist after a race), so the caller is offered the accept.
  private static FriendRelation RelationOf(bool friend, bool incoming, bool outgoing) =>
    friend ? FriendRelation.Friend
    : incoming ? FriendRelation.Incoming
    : outgoing ? FriendRelation.Outgoing
    : FriendRelation.None;

  private static string EscapeLike(string s) =>
    s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

  private static Task<List<FriendRequestPrincipal>> RequestPrincipals(IQueryable<FriendRequestData> requests) =>
    requests.AsNoTracking()
      .OrderByDescending(x => x.CreatedAtUtc)
      .Select(x => new FriendRequestPrincipal(
        new FriendUser(x.FromUserId, x.From!.Username),
        new FriendUser(x.ToUserId, x.To!.Username),
        x.CreatedAtUtc))
      .ToListAsync();
}
