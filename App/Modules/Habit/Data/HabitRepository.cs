using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Habit;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Habit.Data
{
    public class HabitRepository(MainDbContext db, ILogger<HabitRepository> logger) : IHabitRepository
    {
        public async Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date)
        {
            try
            {
                logger.LogInformation("Retrieving active habits for UserId: {UserId}, Date: {Date}", userId, date);
                
                // Join habit with current version to get active habits with details
                var data = await (from h in db.Habits
                                 join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                                 where h.UserId == userId && 
                                       h.DeletedAt == null &&
                                       hv.StartDate <= date && 
                                       hv.EndDate >= date
                                 select hv).AsNoTracking().ToListAsync();
                
                logger.LogInformation("Retrieved {Count} active habits for UserId: {UserId}", data.Count, userId);
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving active habits for UserId: {UserId}, Date: {Date}", userId, date);
                return e;
            }
        }

        public async Task<Result<HabitPrincipal?>> GetHabit(Guid habitId)
        {
            try
            {
                logger.LogInformation("Retrieving habit by Id: {HabitId}", habitId);
                var data = await db.Habits
                    .AsNoTracking()
                    .Where(x => x.Id == habitId && x.DeletedAt == null)
                    .FirstOrDefaultAsync();
                
                if (data == null)
                {
                    logger.LogWarning("No habit found for Id: {HabitId}", habitId);
                    return (HabitPrincipal?)null;
                }
                
                logger.LogInformation("Retrieved habit with version {Version} for Id: {HabitId}", data.Version, habitId);
                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving habit by Id: {HabitId}", habitId);
                return e;
            }
        }

        public async Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId)
        {
            try
            {
                logger.LogInformation("Retrieving current version for HabitId: {HabitId}", habitId);
                
                // Join to get current version details
                var data = await (from h in db.Habits
                                 join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                                 where h.Id == habitId && h.UserId == userId && h.DeletedAt == null
                                 select hv).AsNoTracking().FirstOrDefaultAsync();
                
                if (data == null)
                {
                    logger.LogWarning("No current version found for HabitId: {HabitId}", habitId);
                    return (HabitVersionPrincipal?)null;
                }
                
                logger.LogInformation("Retrieved current version {Version} for HabitId: {HabitId}", data.Version, habitId);
                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving current version for HabitId: {HabitId}", habitId);
                return e;
            }
        }

        public async Task<Result<HabitPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                logger.LogInformation("Creating habit for UserId: {UserId}, Task: {Task}", userId, versionRecord.Task);
                
                // Create main habit record with version 1
                var habitData = new HabitData
                {
                    UserId = userId,
                    Version = 1
                };
                var habitResult = db.Habits.Add(habitData);
                
                // Create first version record using mapper
                var versionData = versionRecord.ToData(habitData.Id, 1);
                db.HabitVersions.Add(versionData);
                
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                logger.LogInformation("Habit created with Id: {Id}, Version: {Version}", 
                    habitData.Id, habitData.Version);
                return habitResult.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create habit for UserId: {UserId}", userId);
                await transaction.RollbackAsync();
                return e;
            }
        }

        public async Task<Result<HabitPrincipal?>> Update(Guid habitId, HabitVersionRecord versionRecord)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                logger.LogInformation("Updating habit version for HabitId: {HabitId}", habitId);
                
                // Lock the habit row and atomically increment version
                var affectedRows = await db.Database.ExecuteSqlAsync(
                    $"UPDATE \"Habits\" SET \"Version\" = \"Version\" + 1 WHERE \"Id\" = {habitId} AND \"DeletedAt\" IS NULL");
                
                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit found or already deleted for HabitId: {HabitId}", habitId);
                    await transaction.RollbackAsync();
                    return (HabitPrincipal?)null;
                }

                // Get the updated version number
                var updatedHabit = await db.Habits
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == habitId);
                
                if (updatedHabit == null)
                {
                    await transaction.RollbackAsync();
                    return (HabitPrincipal?)null;
                }

                // Create new version record using mapper
                var versionData = versionRecord.ToData(habitId, updatedHabit.Version);
                db.HabitVersions.Add(versionData);
                
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                logger.LogInformation("Habit version updated to {Version} for HabitId: {HabitId}", 
                    updatedHabit.Version, habitId);
                return updatedHabit.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update habit version for HabitId: {HabitId}", habitId);
                await transaction.RollbackAsync();
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(Guid habitId, string userId)
        {
            try
            {
                logger.LogInformation("Soft deleting habit for HabitId: {HabitId}, UserId: {UserId}", habitId, userId);
                
                var affectedRows = await db.Database.ExecuteSqlAsync(
                    $"UPDATE \"Habits\" SET \"DeletedAt\" = {DateTime.UtcNow} WHERE \"Id\" = {habitId} AND \"UserId\" = {userId} AND \"DeletedAt\" IS NULL");
                
                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit found for delete, HabitId: {HabitId}, UserId: {UserId}", habitId, userId);
                    return (Unit?)null;
                }

                logger.LogInformation("Soft deleted habit for HabitId: {HabitId}", habitId);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete habit for HabitId: {HabitId}, UserId: {UserId}", habitId, userId);
                return e;
            }
        }

        public async Task<Result<int>> CreateFailedExecutions(List<string> userIds, DateOnly date)
        {
            try
            {
                logger.LogInformation("Creating failed executions for {UserCount} users on date: {Date}", userIds.Count, date);
                
                // Single atomic INSERT ... SELECT with LEFT JOIN to prevent race conditions
                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), hv.""Id"", {date}, {(int)ExecutionStatus.Failed}, false
                    FROM ""Habits"" h
                    JOIN ""HabitVersions"" hv ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                    LEFT JOIN ""HabitExecutions"" he ON he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                    WHERE h.""UserId"" = ANY({userIds}) 
                      AND h.""DeletedAt"" IS NULL 
                      AND hv.""StartDate"" <= {date}
                      AND hv.""EndDate"" >= {date}
                      AND he.""Id"" IS NULL
                ");

                logger.LogInformation("Created {Count} failed executions for date: {Date}", affectedRows, date);
                return affectedRows;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create failed executions for date: {Date}", date);
                return e;
            }
        }

        public async Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, DateOnly date, string? notes)
        {
            try
            {
                logger.LogInformation("Completing habit for UserId: {UserId}, HabitId: {HabitId}, Date: {Date}", userId, habitId, date);
                
                // Atomic INSERT ... SELECT to get current version and create execution
                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""CompletedAt"", ""Notes"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), hv.""Id"", {date}, {(int)ExecutionStatus.Completed}, {DateTime.UtcNow}, {notes}, false
                    FROM ""Habits"" h
                    JOIN ""HabitVersions"" hv ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                    WHERE h.""Id"" = {habitId}
                      AND h.""UserId"" = {userId}
                      AND h.""DeletedAt"" IS NULL 
                      AND hv.""StartDate"" <= {date}
                      AND hv.""EndDate"" >= {date}
                      AND NOT EXISTS (
                          SELECT 1 FROM ""HabitExecutions"" he 
                          WHERE he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                      )
                ");

                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit execution created - habit not found, not active, or already completed for HabitId: {HabitId}, Date: {Date}", habitId, date);
                    throw new InvalidOperationException("Habit not found, not active for this date, or already completed");
                }

                // Get the created execution to return
                var execution = await (from h in db.Habits
                                     join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                                     join he in db.HabitExecutions on hv.Id equals he.HabitVersionId
                                     where h.Id == habitId && h.UserId == userId && he.Date == date
                                     select he).AsNoTracking().FirstOrDefaultAsync();

                logger.LogInformation("Habit completed for HabitId: {HabitId}, Date: {Date}", habitId, date);
                return execution!.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to complete habit for HabitId: {HabitId}, UserId: {UserId}, Date: {Date}", habitId, userId, date);
                return e;
            }
        }

        public async Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date)
        {
            try
            {
                logger.LogInformation("Retrieving daily executions for UserId: {UserId}, Date: {Date}", userId, date);
                
                // Get all executions for user's habits on the specified date
                var data = await (from h in db.Habits
                                join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                                join he in db.HabitExecutions on hv.Id equals he.HabitVersionId
                                where h.UserId == userId && he.Date == date && h.DeletedAt == null
                                select he).AsNoTracking().ToListAsync();
                
                logger.LogInformation("Retrieved {Count} executions for UserId: {UserId}, Date: {Date}", data.Count, userId, date);
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving daily executions for UserId: {UserId}, Date: {Date}", userId, date);
                return e;
            }
        }
    }
}
