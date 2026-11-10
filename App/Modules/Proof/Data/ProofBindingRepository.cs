using App.StartUp.Database;
using CSharp_Result;
using Domain.Proof;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Proof.Data;

public class ProofBindingRepository(MainDbContext db, ILogger<ProofBindingRepository> logger)
  : IProofBindingRepository
{
  public async Task<Result<ProofBinding?>> GetForHabit(Guid habitId)
  {
    try
    {
      var data = await db.ProofBindings.AsNoTracking().Include(x => x.Habit)
        .Where(x => x.HabitId == habitId && x.Habit!.DeletedAt == null)
        .FirstOrDefaultAsync();
      return data?.ToDomain();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetForHabit proof binding failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<List<ProofBinding>>> ListForUser(string userId, string pluginKey)
  {
    try
    {
      var data = await db.ProofBindings.AsNoTracking().Include(x => x.Habit)
        .Where(x => x.PluginKey == pluginKey && x.Habit!.UserId == userId && x.Habit.DeletedAt == null)
        .ToListAsync();
      return data.Select(x => x.ToDomain()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListForUser proof bindings failed for UserId={UserId} PluginKey={PluginKey}",
        userId, pluginKey);
      throw;
    }
  }

  public async Task<Result<ProofBindingPrincipal>> Upsert(Guid habitId, ProofBindingRecord record)
  {
    try
    {
      var data = await db.ProofBindings.Where(x => x.HabitId == habitId).FirstOrDefaultAsync();
      if (data == null) db.ProofBindings.Add(data = record.ToData(habitId));
      else data.ToData(record);
      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Upsert proof binding failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<Unit?>> Delete(Guid habitId)
  {
    try
    {
      var affected = await db.ProofBindings.Where(x => x.HabitId == habitId).ExecuteDeleteAsync();
      if (affected == 0) return (Unit?)null;
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete proof binding failed for HabitId={HabitId}", habitId);
      throw;
    }
  }
}
