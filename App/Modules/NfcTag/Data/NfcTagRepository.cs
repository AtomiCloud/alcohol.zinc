using App.Error.V1;
using App.Modules.HabitExecution.Data;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Habit;
using Domain.NfcTag;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.NfcTag.Data;

public class NfcTagRepository(MainDbContext db, ILogger<NfcTagRepository> logger) : INfcTagRepository
{
  public async Task<Result<NfcTagPrincipal?>> Get(string tagId)
  {
    try
    {
      var data = await db.NfcTags.AsNoTracking().Where(x => x.Id == tagId).FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get NFC tag failed for Id={Id}", tagId);
      throw;
    }
  }

  public async Task<Result<NfcTagPrincipal>> Upsert(string tagId, string userId, Guid habitId)
  {
    try
    {
      var data = await db.NfcTags.Where(x => x.Id == tagId).FirstOrDefaultAsync();
      if (data == null)
      {
        logger.LogInformation("Claiming NFC tag Id={Id} for UserId={UserId} HabitId={HabitId}", tagId, userId, habitId);
        data = new NfcTagData { Id = tagId, UserId = userId, HabitId = habitId };
        db.NfcTags.Add(data);
      }
      else if (data.UserId != userId)
      {
        // Ownership re-checked here, not just in the service: the service's
        // check reads a snapshot, so a competing claim committed between that
        // read and this one would otherwise be silently re-pointed.
        logger.LogWarning("NFC tag claim conflict for Id={Id}: owned by another user", tagId);
        return new EntityConflict("NFC tag is already linked by another user", typeof(NfcTagPrincipal))
          .ToException();
      }
      else
      {
        logger.LogInformation("Re-linking NFC tag Id={Id} for UserId={UserId} to HabitId={HabitId}", tagId, userId, habitId);
        data.ToData(new NfcTagRecord { HabitId = habitId });
      }

      await db.SaveChangesAsync();
      return data.ToPrincipal();
    }
    catch (UniqueConstraintException e)
    {
      // Two users raced to claim the same unclaimed tag — first insert wins.
      logger.LogError(e, "NFC tag claim conflict for Id={Id} UserId={UserId}", tagId, userId);
      return new EntityConflict("NFC tag is already linked by another user", typeof(NfcTagPrincipal))
        .ToException();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Upsert NFC tag failed for Id={Id} UserId={UserId}", tagId, userId);
      throw;
    }
  }

  public async Task<Result<Unit?>> Delete(string tagId, string userId)
  {
    try
    {
      var affected = await db.NfcTags
        .Where(x => x.Id == tagId && x.UserId == userId)
        .ExecuteDeleteAsync();
      if (affected == 0) return (Unit?)null;
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete NFC tag failed for Id={Id}", tagId);
      throw;
    }
  }

  public async Task<Result<HabitExecutionPrincipal?>> GetExecutionForDate(Guid habitId, DateOnly date)
  {
    try
    {
      // Postgres sorts NULLS FIRST on DESC, so coalesce: a completed row must
      // win over same-day failure/vacation rows (which have no CompletedAt).
      var data = await db.HabitExecutions.AsNoTracking()
        .Where(x => x.Date == date && x.HabitVersion!.HabitId == habitId)
        .OrderByDescending(x => x.CompletedAt ?? DateTime.MinValue)
        .FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetExecutionForDate failed for HabitId={HabitId} Date={Date}", habitId, date);
      throw;
    }
  }
}
