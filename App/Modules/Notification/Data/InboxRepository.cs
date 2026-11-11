using App.StartUp.Database;
using CSharp_Result;
using Domain.Notification;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Notification.Data;

public class InboxRepository(MainDbContext db, ILogger<InboxRepository> logger) : IInboxRepository
{
  public async Task<Result<InboxNotificationPrincipal>> Create(string userId, InboxNotificationRecord record)
  {
    var data = record.ToData(userId);
    // Postgres keeps microseconds: truncate so what Create returns equals what Search later reads.
    data.CreatedAtUtc = new DateTime(data.CreatedAtUtc.Ticks - data.CreatedAtUtc.Ticks % 10, DateTimeKind.Utc);
    try
    {
      db.InboxNotifications.Add(data);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (UniqueConstraintException) when (record.DedupeKey != null)
    {
      // Already posted to this user (an outbox retry, or a racing post): hand back that one.
      db.Entry(data).State = EntityState.Detached;
      try
      {
        var existing = await db.InboxNotifications.AsNoTracking()
          .Where(x => x.UserId == userId && x.DedupeKey == record.DedupeKey)
          .FirstAsync();
        logger.LogInformation("Inbox notification deduped for UserId={UserId} DedupeKey={DedupeKey} Id={Id}",
          userId, record.DedupeKey, existing.Id);
        return existing.ToPrincipal();
      }
      catch (Exception e)
      {
        logger.LogError(e, "Reading deduped inbox notification failed for UserId={UserId} DedupeKey={DedupeKey}",
          userId, record.DedupeKey);
        throw;
      }
    }
    catch (Exception e)
    {
      db.Entry(data).State = EntityState.Detached;
      logger.LogError(e, "Create inbox notification failed for UserId={UserId} Kind={Kind}", userId, record.Kind);
      throw;
    }
  }

  public async Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search)
  {
    try
    {
      var query = db.InboxNotifications.AsNoTracking().Where(x => x.UserId == search.UserId);
      if (search.UnreadOnly) query = query.Where(x => x.ReadAtUtc == null);
      // Id breaks ties so pages never overlap or skip rows created at the same instant.
      var data = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .ThenByDescending(x => x.Id)
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToListAsync();
      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Search inbox failed for UserId={UserId}", search.UserId);
      throw;
    }
  }

  public async Task<Result<int>> UnreadCount(string userId)
  {
    try
    {
      return await db.InboxNotifications.AsNoTracking()
        .Where(x => x.UserId == userId && x.ReadAtUtc == null)
        .CountAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "UnreadCount inbox failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<Unit?>> MarkRead(string userId, Guid id, DateTime atUtc)
  {
    try
    {
      var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
      // Already-read notifications still match (so the call is idempotent) but keep their first read time.
      var affected = await db.InboxNotifications
        .Where(x => x.Id == id && x.UserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, x => x.ReadAtUtc ?? at));
      if (affected == 0) return (Unit?)null;
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkRead inbox failed for UserId={UserId} Id={Id}", userId, id);
      throw;
    }
  }

  public async Task<Result<int>> MarkAllRead(string userId, DateTime atUtc)
  {
    try
    {
      var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
      return await db.InboxNotifications
        .Where(x => x.UserId == userId && x.ReadAtUtc == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, at));
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkAllRead inbox failed for UserId={UserId}", userId);
      throw;
    }
  }
}
