using App.StartUp.Database;
using CSharp_Result;
using Domain.Protection;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Protection.Data;

public class ProtectionRepository(MainDbContext db, ILogger<ProtectionRepository> logger) : IProtectionRepository
{
  public async Task<Result<UserProtectionPrincipal?>> GetProtection(string userId)
  {
    try
    {
      var data = await db.UserProtections.AsNoTracking().Where(x => x.UserId == userId).FirstOrDefaultAsync();
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetProtection failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<UserProtectionPrincipal>> UpsertProtection(string userId)
  {
    try
    {
      var data = await db.UserProtections.Where(x => x.UserId == userId).FirstOrDefaultAsync();
      if (data == null)
      {
        data = new UserProtectionData { UserId = userId, FreezeCurrent = 0 };
        db.UserProtections.Add(data);
        await db.SaveChangesAsync();
      }
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "UpsertProtection failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<bool>> TryConsumeFreeze(string userId, DateOnly date)
  {
    try
    {
      // Use transaction to ensure (balance > 0) and unique consumption per day
      await using var tx = await db.Database.BeginTransactionAsync();

      // Has consumed today already?
      var consumed = await db.FreezeConsumptions.AsNoTracking()
        .AnyAsync(x => x.UserId == userId && x.Date == date);
      if (consumed)
      {
        await tx.CommitAsync();
        return true;
      }

      // Lock row (if exists); otherwise insert Zero row then lock
      var protection = await db.UserProtections.Where(x => x.UserId == userId).FirstOrDefaultAsync();
      if (protection == null)
      {
        protection = new UserProtectionData { UserId = userId, FreezeCurrent = 0 };
        db.UserProtections.Add(protection);
        await db.SaveChangesAsync();
      }

      if (protection.FreezeCurrent <= 0)
      {
        await tx.RollbackAsync();
        return false;
      }

      protection.FreezeCurrent -= 1;
      protection.UpdatedAt = DateTime.UtcNow;
      db.FreezeConsumptions.Add(new FreezeConsumptionData { UserId = userId, Date = date });
      await db.SaveChangesAsync();
      await tx.CommitAsync();
      return true;
    }
    catch (Exception e)
    {
      logger.LogError(e, "TryConsumeFreeze failed for UserId={UserId} Date={Date}", userId, date);
      throw;
    }
  }

  public async Task<Result<Unit>> IncrementFreeze(string userId, int n)
  {
    try
    {
      var data = await db.UserProtections.Where(x => x.UserId == userId).FirstOrDefaultAsync();
      if (data == null)
      {
        data = new UserProtectionData { UserId = userId, FreezeCurrent = 0 };
        db.UserProtections.Add(data);
      }
      data.FreezeCurrent += Math.Max(0, n);
      data.UpdatedAt = DateTime.UtcNow;
      await db.SaveChangesAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "IncrementFreeze failed for UserId={UserId} N={N}", userId, n);
      throw;
    }
  }

  public async Task<Result<bool>> RecordFreezeAwardIfAbsent(Guid habitId, DateOnly weekStart)
  {
    try
    {
      // insert if not exists via unique index
      var exists = await db.FreezeAwards.AsNoTracking()
        .AnyAsync(x => x.HabitId == habitId && x.WeekStart == weekStart);
      if (exists) return true;
      db.FreezeAwards.Add(new FreezeAwardData { HabitId = habitId, WeekStart = weekStart });
      await db.SaveChangesAsync();
      return true;
    }
    catch (Exception e)
    {
      logger.LogError(e, "RecordFreezeAwardIfAbsent failed for HabitId={HabitId} WeekStart={WeekStart}", habitId, weekStart);
      throw;
    }
  }

  public async Task<Result<Unit>> ClampFreezeToCap(string userId, int cap)
  {
    try
    {
      var data = await db.UserProtections.Where(x => x.UserId == userId).FirstOrDefaultAsync();
      if (data == null)
      {
        data = new UserProtectionData { UserId = userId, FreezeCurrent = 0 };
        db.UserProtections.Add(data);
      }
      if (data.FreezeCurrent > cap)
      {
        data.FreezeCurrent = Math.Max(0, cap);
        data.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
      }
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ClampFreezeToCap failed for UserId={UserId} Cap={Cap}", userId, cap);
      throw;
    }
  }
}
