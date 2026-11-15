using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
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
        .Where(x => x.Status == FriendRequestStatus.Pending && x.ToUserId == userId && ids.Contains(x.FromUserId))
        .Select(x => x.FromUserId).ToListAsync()).ToHashSet();
      var outgoing = (await db.FriendRequests.AsNoTracking()
        .Where(x => x.Status == FriendRequestStatus.Pending && x.FromUserId == userId && ids.Contains(x.ToUserId))
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
        .AnyAsync(x => x.Status == FriendRequestStatus.Pending && x.FromUserId == otherUserId && x.ToUserId == userId);
      var outgoing = await db.FriendRequests.AsNoTracking()
        .AnyAsync(x => x.Status == FriendRequestStatus.Pending && x.FromUserId == userId && x.ToUserId == otherUserId);
      return RelationOf(friend, incoming, outgoing);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Relation failed for UserId={UserId} OtherUserId={OtherUserId}", userId, otherUserId);
      throw;
    }
  }

  public async Task<Result<FriendRequestChange>> CreateRequest(string fromUserId, string toUserId)
  {
    FriendRequestData? data = null;
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      await LockPair(fromUserId, toUserId);
      if (await db.Friendships.AnyAsync(x => x.UserId == fromUserId && x.FriendUserId == toUserId))
        return new FriendRequestChange(null, FriendRelation.Friend, false);

      // Existing opposite Pending rows from the old writer retain both IDs. Incoming wins,
      // matching asking-back-is-accepting; acceptance reconciles the sender's outgoing row.
      data = await Requests().Where(x => x.Status == FriendRequestStatus.Pending &&
          ((x.FromUserId == fromUserId && x.ToUserId == toUserId) ||
           (x.FromUserId == toUserId && x.ToUserId == fromUserId)))
        .OrderBy(x => x.ToUserId == fromUserId ? 0 : 1).FirstOrDefaultAsync();
      if (data?.FromUserId == fromUserId)
        return new FriendRequestChange(data.ToPrincipal(), FriendRelation.Outgoing, false);
      var relation = FriendRelation.Outgoing;
      if (data != null)
      {
        await AcceptOccurrence(data);
        relation = FriendRelation.Friend;
      }
      else
      {
        data = new FriendRequestData { FromUserId = fromUserId, ToUserId = toUserId };
        db.FriendRequests.Add(data);
      }
      await db.SaveChangesAsync();
      // Load the database-generated identity and participant names for the caller/notification.
      var principal = await Requests().Where(x => x.Id == data.Id).SingleAsync();
      await tx.CommitAsync();
      return new FriendRequestChange(principal.ToPrincipal(), relation, true);
    }
    catch (Exception e) when (UserLock.IsLockTimeout(e))
    {
      return new UserBusyException(fromUserId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "CreateRequest failed for FromUserId={FromUserId} ToUserId={ToUserId}", fromUserId,
        toUserId);
      throw;
    }
    finally
    {
      if (data != null) db.Entry(data).State = EntityState.Detached;
    }
  }

  public async Task<Result<FriendRequests>> ListRequests(string userId)
  {
    try
    {
      var pending = db.FriendRequests.Where(x => x.Status == FriendRequestStatus.Pending);
      var incoming = await RequestPrincipals(pending.Where(x => x.ToUserId == userId));
      var outgoing = await RequestPrincipals(pending.Where(x => x.FromUserId == userId));
      return new FriendRequests(incoming, outgoing);
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListRequests failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<FriendRequestPrincipal>> GetRequest(string userId, Guid requestId)
  {
    try
    {
      var data = await Requests().SingleOrDefaultAsync(x => x.Id == requestId &&
        (x.FromUserId == userId || x.ToUserId == userId));
      if (data == null) return MissingRequest(requestId);
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetRequest failed for UserId={UserId} RequestId={RequestId}", userId, requestId);
      throw;
    }
  }

  public async Task<Result<FriendRequestChange>> ResolveRequest(string userId, Guid requestId, FriendRequestStatus status)
  {
    try
    {
      // Read only to locate the immutable participants. All mutable state is re-read after locking.
      var located = await Requests().SingleOrDefaultAsync(x => x.Id == requestId &&
        (x.FromUserId == userId || x.ToUserId == userId));
      if (located == null) return MissingRequest(requestId);
      await using var tx = await db.Database.BeginTransactionAsync();
      await LockPair(located.FromUserId, located.ToUserId);
      var data = await Requests().SingleOrDefaultAsync(x => x.Id == requestId);
      if (data == null) return MissingRequest(requestId);
      var allowed = status == FriendRequestStatus.Cancelled ? data.FromUserId == userId : data.ToUserId == userId;
      if (!allowed) return new FriendRequestException(FriendRequestError.WrongRole);
      if (data.Status != FriendRequestStatus.Pending)
      {
        if (data.Status != status) return new FriendRequestException(FriendRequestError.Resolved);
        return new FriendRequestChange(data.ToPrincipal(), FriendRelation.None, false);
      }

      if (status == FriendRequestStatus.Accepted) await AcceptOccurrence(data);
      else
        await db.FriendRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Status, status).SetProperty(x => x.ResolvedAtUtc, DateTime.UtcNow));
      var resolved = await Requests().SingleAsync(x => x.Id == requestId);
      await tx.CommitAsync();
      return new FriendRequestChange(resolved.ToPrincipal(),
        status == FriendRequestStatus.Accepted ? FriendRelation.Friend : FriendRelation.None, true);
    }
    catch (Exception e) when (UserLock.IsLockTimeout(e))
    {
      return new UserBusyException(userId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "ResolveRequest failed for UserId={UserId} RequestId={RequestId}", userId, requestId);
      throw;
    }
  }

  private async Task AcceptOccurrence(FriendRequestData request)
  {
    // Pair locks are already held. Both directions and the outcome are in the same transaction.
    var now = DateTime.UtcNow;
    await db.Database.ExecuteSqlAsync($"""
      INSERT INTO "Friendships" ("Id", "UserId", "FriendUserId", "CreatedAtUtc") VALUES
        (gen_random_uuid(), {request.FromUserId}, {request.ToUserId}, {now}),
        (gen_random_uuid(), {request.ToUserId}, {request.FromUserId}, {now})
      ON CONFLICT ("UserId", "FriendUserId") DO NOTHING
      """);
    await db.FriendRequests.Where(x => x.Id == request.Id).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, FriendRequestStatus.Accepted).SetProperty(x => x.ResolvedAtUtc, now));
    // Only a new acceptance may supersede the acceptor's own outgoing Pending occurrence.
    // This is a decision now, never a guessed/backdated historical outcome.
    await db.FriendRequests.Where(x => x.Status == FriendRequestStatus.Pending &&
        x.FromUserId == request.ToUserId && x.ToUserId == request.FromUserId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FriendRequestStatus.Cancelled)
        .SetProperty(x => x.ResolvedAtUtc, now));
  }

  private IQueryable<FriendRequestData> Requests() =>
    db.FriendRequests.AsNoTracking().Include(x => x.From).Include(x => x.To);

  private static NotFoundException MissingRequest(Guid id) =>
    new("Friend request not found", typeof(FriendRequestPrincipal), id.ToString());

  private async Task LockPair(string first, string second)
  {
    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
    foreach (var id in new[] { first, second }.Order(StringComparer.Ordinal))
      await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE");
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
      await LockPair(userId, friendUserId);
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
    catch (Exception e) when (UserLock.IsLockTimeout(e))
    {
      return new UserBusyException(userId);
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
        x.Id,
        new FriendUser(x.FromUserId, x.From!.Username),
        new FriendUser(x.ToUserId, x.To!.Username),
        x.CreatedAtUtc, x.Status, x.ResolvedAtUtc))
      .ToListAsync();
}
