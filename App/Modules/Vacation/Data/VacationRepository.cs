using App.Error.V1;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Entitlement;
using Domain.Exceptions;
using Domain.Vacation;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Vacation.Data;

public class VacationRepository(MainDbContext db, ILogger<VacationRepository> logger) : IVacationRepository
{
  public async Task<Result<VacationPrincipal?>> Get(Guid id)
  {
    try
    {
      var data = await db.VacationPeriods.AsNoTracking().Where(x => x.Id == id).FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get vacation failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<VacationPrincipal?>> Get(Guid id, string? userId)
  {
    try
    {
      var q = db.VacationPeriods.AsNoTracking().Where(x => x.Id == id);
      if (!string.IsNullOrEmpty(userId)) q = q.Where(x => x.UserId == userId);
      var data = await q.FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get vacation failed for Id={Id} UserId={UserId}", id, userId);
      throw;
    }
  }

  public async Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record, Quota windows)
  {
    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
      logger.LogInformation("Creating vacation window for UserId={UserId} {@Record}", userId, record);
      // Count and insert under the user's lock, so concurrent creates can't both pass the count.
      await db.Database.LockUser(userId);
      if ((await CountWindowsForYear(userId, record.StartDate.Year)).Get() >= windows.Limit)
      {
        await tx.RollbackAsync();
        return new TierInsufficient(windows.Tier, windows.Key, windows.Limit).ToException();
      }

      var data = record.ToData(userId);
      db.VacationPeriods.Add(data);
      await db.SaveChangesAsync();
      await tx.CommitAsync();
      return data.ToPrincipal();
    }
    catch (Exception e) when (UserLock.IsLockTimeout(e))
    {
      logger.LogWarning("Create vacation for UserId={UserId} timed out waiting for the user's lock", userId);
      await tx.RollbackAsync();
      db.ChangeTracker.Clear();
      return new UserBusyException(userId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Create vacation failed for UserId={UserId}", userId);
      await tx.RollbackAsync();
      db.ChangeTracker.Clear();
      throw;
    }
  }

  public async Task<Result<VacationPrincipal?>> Update(Guid id, VacationRecord record)
  {
    try
    {
      var data = await db.VacationPeriods.Where(x => x.Id == id).FirstOrDefaultAsync();
      if (data == null) return (VacationPrincipal?)null;
      data = data.ToData(record);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Update vacation failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit?>> Delete(Guid id, string userId)
  {
    try
    {
      var affected = await db.VacationPeriods
        .Where(x => x.Id == id && x.UserId == userId)
        .ExecuteDeleteAsync();
      if (affected == 0) return (Unit?)null;
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete vacation failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<List<VacationPrincipal>>> Search(VacationSearch search)
  {
    try
    {
      var q = db.VacationPeriods.AsNoTracking().Where(x => x.UserId == search.UserId);
      if (search.Year != null)
      {
        var y = search.Year.Value;
        q = q.Where(x => x.StartDate.Year == y || x.EndDate.Year == y);
      }
      var items = await q
        .OrderByDescending(x => x.StartDate)
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToListAsync();
      return items.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Search vacation failed for UserId={UserId}", search.UserId);
      throw;
    }
  }

  public async Task<Result<List<VacationPrincipal>>> ListActiveForUserOnDate(string userId, DateOnly date)
  {
    try
    {
      var items = await db.VacationPeriods.AsNoTracking()
        .Where(x => x.UserId == userId && x.StartDate <= date && x.EndDate >= date)
        .ToListAsync();
      return items.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListActiveForUserOnDate failed for UserId={UserId} Date={Date}", userId, date);
      throw;
    }
  }

  public async Task<Result<int>> CountWindowsForYear(string userId, int year)
  {
    try
    {
      var count = await db.VacationPeriods.AsNoTracking()
        // A vacation uses one window, in the year it starts, even when it spans New Year.
        .Where(x => x.UserId == userId && x.StartDate.Year == year)
        .CountAsync();
      return count;
    }
    catch (Exception e)
    {
      logger.LogError(e, "CountWindowsForYear failed for UserId={UserId} Year={Year}", userId, year);
      throw;
    }
  }

  public async Task<Result<bool>> HasOverlap(string userId, DateOnly start, DateOnly end)
  {
    try
    {
      var overlap = await db.VacationPeriods.AsNoTracking()
        .AnyAsync(x => x.UserId == userId && start <= x.EndDate && end >= x.StartDate);
      return overlap;
    }
    catch (Exception e)
    {
      logger.LogError(e, "HasOverlap failed for UserId={UserId} Start={Start} End={End}", userId, start, end);
      throw;
    }
  }
}
