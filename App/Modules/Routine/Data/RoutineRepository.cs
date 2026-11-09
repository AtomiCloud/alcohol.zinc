using App.StartUp.Database;
using CSharp_Result;
using Domain.Routine;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Routine.Data;

public class RoutineRepository(MainDbContext db, ILogger<RoutineRepository> logger) : IRoutineRepository
{
  public async Task<Result<RoutinePrincipal?>> Get(Guid id)
  {
    try
    {
      var data = await db.Routines.AsNoTracking()
        .Where(x => x.Id == id && x.DeletedAt == null)
        .FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get routine failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<RoutinePrincipal?>> Get(Guid id, string? userId)
  {
    try
    {
      var q = db.Routines.AsNoTracking().Where(x => x.Id == id && x.DeletedAt == null);
      if (!string.IsNullOrEmpty(userId)) q = q.Where(x => x.UserId == userId);
      var data = await q.FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get routine failed for Id={Id} UserId={UserId}", id, userId);
      throw;
    }
  }

  public async Task<Result<RoutinePrincipal>> Create(string userId, RoutineRecord record)
  {
    try
    {
      logger.LogInformation("Creating routine for UserId={UserId} {@Record}", userId, record);
      var data = record.ToData(userId);
      db.Routines.Add(data);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Create routine failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<RoutinePrincipal?>> Update(Guid id, RoutineRecord record)
  {
    try
    {
      var data = await db.Routines.Where(x => x.Id == id && x.DeletedAt == null).FirstOrDefaultAsync();
      if (data == null) return (RoutinePrincipal?)null;
      data = data.ToData(record);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Update routine failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit?>> Delete(Guid id, string userId)
  {
    await using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
      logger.LogInformation("Soft deleting routine Id={Id} UserId={UserId}", id, userId);
      var now = DateTime.UtcNow;

      var affected = await db.Routines
        .Where(x => x.Id == id && x.UserId == userId && x.DeletedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, now));
      if (affected == 0)
      {
        await transaction.RollbackAsync();
        return (Unit?)null;
      }

      // Same soft delete as HabitRepository.Delete, applied to every live habit in the routine
      var habitIds = await db.Habits
        .Where(h => h.RoutineId == id && h.UserId == userId && h.DeletedAt == null)
        .Select(h => h.Id)
        .ToListAsync();
      if (habitIds.Count > 0)
      {
        await db.Habits
          .Where(h => habitIds.Contains(h.Id) && h.DeletedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(h => h.DeletedAt, now));

        // Soft delete never fires the NfcTags FK cascade, so release the tags explicitly
        await db.NfcTags.Where(x => habitIds.Contains(x.HabitId)).ExecuteDeleteAsync();
      }

      await transaction.CommitAsync();
      logger.LogInformation("Soft deleted routine Id={Id} with {Count} habits", id, habitIds.Count);
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete routine failed for Id={Id} UserId={UserId}", id, userId);
      await transaction.RollbackAsync();
      throw;
    }
  }

  public async Task<Result<List<RoutinePrincipal>>> Search(RoutineSearch search)
  {
    try
    {
      var q = db.Routines.AsNoTracking().Where(x => x.DeletedAt == null);
      if (!string.IsNullOrWhiteSpace(search.UserId))
        q = q.Where(x => x.UserId == search.UserId);
      if (!string.IsNullOrWhiteSpace(search.Name))
        q = q.Where(x => EF.Functions.ILike(x.Name, $"%{search.Name}%"));

      var items = await q
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.Id)
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToListAsync();
      return items.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Search routine failed for {@Search}", search);
      throw;
    }
  }
}
