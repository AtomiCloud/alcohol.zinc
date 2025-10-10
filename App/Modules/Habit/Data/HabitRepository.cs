using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Habit.Data
{
    public class HabitRepository(MainDbContext db, ILogger<HabitRepository> logger) : IHabitRepository
    {
        public async Task<Result<int>> CountHabitsForUser(string userId)
        {
            try
            {
                var count = await db.Habits.AsNoTracking().Where(x => x.UserId == userId && x.DeletedAt == null).CountAsync();
                return count;
            }
            catch (Exception e)
            {
                logger.LogError(e, "CountHabitsForUser failed for UserId={UserId}", userId);
                throw;
            }
        }

        public async Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd)
        {
            try
            {
                var cnt = await (from he in db.HabitExecutions
                                 join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                                 join h in db.Habits on hv.HabitId equals h.Id
                                 where h.UserId == userId && h.DeletedAt == null
                                       && he.Date >= monthStart && he.Date <= monthEnd
                                       && he.Status == App.Modules.HabitExecution.Data.HabitExecutionStatusData.Skipped
                                 select he.Id)
                    .CountAsync();
                return cnt;
            }
            catch (Exception e)
            {
                logger.LogError(e, "CountUserSkipsForMonth failed for UserId={UserId}", userId);
                throw;
            }
        }

        public async Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersionsByIds(List<Guid> habitIds, DateOnly date)
        {
            try
            {
                var dayOfWeek = date.DayOfWeek.ToString();
                var data = await db.HabitVersions
                    .FromSqlRaw(@"
                        SELECT hv.* FROM ""HabitVersions"" hv
                        JOIN ""Habits"" h ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                        WHERE h.""Id"" = ANY({0})
                          AND h.""DeletedAt"" IS NULL
                          AND h.""Enabled"" = true
                          AND hv.""DaysOfWeek"" @> ARRAY[{1}]", habitIds, dayOfWeek)
                    .AsNoTracking()
                    .ToListAsync();
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "GetActiveHabitVersionsByIds failed");
                throw;
            }
        }
        public async Task<Result<int>> CreateExecutionsForVersionsWithStatus(List<Guid> habitVersionIds, DateOnly date, Domain.Habit.ExecutionStatus status)
        {
            try
            {
                if (habitVersionIds.Count == 0) return 0;

                var statusData = status.ToDataStatus();

                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), hv.""Id"", {date}, {(int)statusData}, false
                    FROM ""HabitVersions"" hv
                    LEFT JOIN ""HabitExecutions"" he ON he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                    WHERE hv.""Id"" = ANY({habitVersionIds})
                      AND he.""Id"" IS NULL
                ");

                return affectedRows;
            }
            catch (Exception e)
            {
                logger.LogError(e, "CreateExecutionsForVersionsWithStatus failed");
                throw;
            }
        }

        public async Task<Result<List<HabitPrincipal>>> GetHabitsByIds(List<Guid> habitIds)
        {
            try
            {
                var items = await db.Habits.AsNoTracking().Where(x => habitIds.Contains(x.Id) && x.DeletedAt == null).ToListAsync();
                return items.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "GetHabitsByIds failed");
                throw;
            }
        }
        public async Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date)
        {
            try
            {
                logger.LogInformation("Retrieving active habits for UserId: {UserId}, Date: {Date}", userId, date);

                // Get day of week from the provided date
                var dayOfWeek = date.DayOfWeek.ToString();
                logger.LogInformation("Filtering habits for day: {DayOfWeek}", dayOfWeek);

                // Join habit with current version to get enabled habits scheduled for this day
                // Using PostgreSQL array contains operator (@>) for DaysOfWeek array
                var data = await db.HabitVersions
                    .FromSqlRaw(@"
                        SELECT hv.* FROM ""HabitVersions"" hv
                        JOIN ""Habits"" h ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                        WHERE h.""UserId"" = {0}
                          AND h.""DeletedAt"" IS NULL
                          AND h.""Enabled"" = true
                          AND hv.""DaysOfWeek"" @> ARRAY[{1}]", userId, dayOfWeek)
                    .AsNoTracking()
                    .ToListAsync();
                
                logger.LogInformation("Retrieved {Count} active habits for UserId: {UserId} on {DayOfWeek}", data.Count, userId, dayOfWeek);
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving active habits for UserId: {UserId}, Date: {Date}", userId, date);
                throw;
            }
        }

        public async Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
        {
            try
            {
                logger.LogInformation("Searching habits with search: {@Search}", habitSearch);

                var query = (from h in db.Habits
                           join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                           where h.DeletedAt == null
                           select new { Habit = h, HabitVersion = hv }).AsNoTracking().AsQueryable();

                if (habitSearch.Id != null)
                    query = query.Where(x => x.Habit.Id == habitSearch.Id);

                if (!string.IsNullOrWhiteSpace(habitSearch.UserId))
                    query = query.Where(x => x.Habit.UserId == habitSearch.UserId);

                if (!string.IsNullOrWhiteSpace(habitSearch.Task))
                    query = query.Where(x => EF.Functions.ILike(x.HabitVersion.Task, $"%{habitSearch.Task}%"));

                if (habitSearch.Enabled != null)
                    query = query.Where(x => x.Habit.Enabled == habitSearch.Enabled);

                var data = await query
                    .Skip(habitSearch.Skip)
                    .Take(habitSearch.Limit)
                    .ToListAsync();

                logger.LogInformation("Retrieved {Count} habits", data.Count);
                return data.Select(x => x.HabitVersion.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed searching habits with search: {@Search}", habitSearch);
                throw;
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
                throw;
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
                throw;
            }
        }

        public async Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                logger.LogInformation("Creating habit for UserId: {UserId}, Task: {Task}", userId, versionRecord.Task);
                
                // Create main habit record with version 1
                var habitData = new HabitData
                {
                    UserId = userId,
                    Version = 1,
                    Enabled = true  // New habits are enabled by default
                };
                db.Habits.Add(habitData);
                
                // Create first version record using mapper
                var versionData = versionRecord.ToData(habitData.Id, 1);
                db.HabitVersions.Add(versionData);
                
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                logger.LogInformation("Habit created with Id: {Id}, Version: {Version}", 
                    habitData.Id, habitData.Version);
                return versionData.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create habit for UserId: {UserId}", userId);
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                logger.LogInformation("Updating habit version and enabled status for HabitId: {HabitId}, Enabled: {Enabled}", habitId, enabled);

                // Atomically increment version and update enabled status using EF Core bulk update
                var affectedRows = await db.Habits
                    .Where(h => h.Id == habitId && h.UserId == userId && h.DeletedAt == null)
                    .ExecuteUpdateAsync(h => h
                        .SetProperty(x => x.Version, x => x.Version + 1)
                        .SetProperty(x => x.Enabled, enabled));
                
                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit found or already deleted for HabitId: {HabitId}", habitId);
                    await transaction.RollbackAsync();
                    return (HabitVersionPrincipal?)null;
                }

                // Get the current version to calculate the new version number
                var currentVersion = await db.Habits
                    .AsNoTracking()
                    .Where(x => x.Id == habitId)
                    .Select(x => x.Version)
                    .FirstAsync();

                // Create new version record using mapper
                var versionData = versionRecord.ToData(habitId, currentVersion);
                db.HabitVersions.Add(versionData);
                
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                logger.LogInformation("Habit version updated to {Version} for HabitId: {HabitId}",
                    currentVersion, habitId);
                return versionData.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update habit version for HabitId: {HabitId}", habitId);
                await transaction.RollbackAsync();
                throw;
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
                throw;
            }
        }

        public async Task<Result<int>> CreateFailedExecutions(List<Guid> habitIds, DateOnly date)
        {
            try
            {
                logger.LogInformation("Creating failed executions for {HabitCount} habits on date: {Date}", habitIds.Count, date);

                // Get day of week from the provided date
                var dayOfWeek = date.DayOfWeek.ToString();
                logger.LogInformation("Creating failures for habits scheduled on: {DayOfWeek}", dayOfWeek);

                // Single atomic INSERT ... SELECT with LEFT JOIN to prevent race conditions
                // Using PostgreSQL array contains operator (@>) for DaysOfWeek array
                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), hv.""Id"", {date}, {(int)App.Modules.HabitExecution.Data.HabitExecutionStatusData.Failed}, false
                    FROM ""Habits"" h
                    JOIN ""HabitVersions"" hv ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                    LEFT JOIN ""HabitExecutions"" he ON he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                    WHERE h.""Id"" = ANY({habitIds})
                      AND h.""DeletedAt"" IS NULL
                      AND h.""Enabled"" = true
                      AND hv.""DaysOfWeek"" @> ARRAY[{dayOfWeek}]
                      AND he.""Id"" IS NULL
                ");

                logger.LogInformation("Created {Count} failed executions for date: {Date} ({DayOfWeek})", affectedRows, date, dayOfWeek);
                return affectedRows;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create failed executions for date: {Date}", date);
                throw;
            }
        }

        public async Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId)
        {
            try
            {
                logger.LogInformation("Getting current date for UserId: {UserId}", userId);

                // Get user's timezone from their configuration
                var habitLevelTimezoneConfig = await (from hv in db.HabitVersions
                                      where hv.Id == habitVersionId
                                      select hv.Timezone).AsNoTracking().FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(habitLevelTimezoneConfig))
                {
                  var ex = new NotFoundException("HabitVersion not found",
                    typeof(HabitExecutionPrincipal), habitVersionId.ToString());
                  logger.LogError(ex, "GetUserCurrentDate failed to find HabitVersion");

                  return ex;
                }
                var habitLevelTimezone = TimeZoneInfo.FindSystemTimeZoneById(habitLevelTimezoneConfig);
                var userDateTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, habitLevelTimezone);
                var userDate = DateOnly.FromDateTime(userDateTime);

                logger.LogInformation("Current date for UserId: {UserId} in timezone {Timezone} is {Date}",
                    userId, habitLevelTimezone, userDate);
                return userDate;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to get current date for UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes)
        {
            try
            {
                logger.LogInformation("Completing habit for UserId: {UserId}, HabitId: {HabitId}, Date: {Date}", userId, habitVersionId, date);

                // Get day of week from the provided date
                var dayOfWeek = date.DayOfWeek.ToString();
                logger.LogInformation("Validating habit is scheduled for: {DayOfWeek}", dayOfWeek);

                // Atomic INSERT ... SELECT to get current version and create execution
                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""CompletedAt"", ""Notes"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), {habitVersionId}, {date}, {(int)App.Modules.HabitExecution.Data.HabitExecutionStatusData.Completed}, {DateTime.UtcNow}, {notes}, false
                    FROM ""HabitVersions"" hv
                    JOIN ""Habits"" h ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                    WHERE hv.""Id"" = {habitVersionId}
                      AND hv.""DaysOfWeek"" @> ARRAY[{dayOfWeek}]
                      AND h.""UserId"" = {userId}
                      AND h.""DeletedAt"" IS NULL
                      AND h.""Enabled"" = true
                      AND NOT EXISTS (
                          SELECT 1 FROM ""HabitExecutions"" he
                          WHERE he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                      )
                ");

                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit execution created - habit not found, not enabled, not scheduled for {DayOfWeek}, or already completed " +
                                      "for HabitVersionId: {HabitVersionId}, Date: {Date}", dayOfWeek, habitVersionId, date);
                    return new NotFoundException("Habit not found, not enabled, not scheduled for this day, or already completed",
                      typeof(HabitExecutionPrincipal), habitVersionId.ToString());
                }

                // Get the created execution to return
                var execution = await (from hv in db.HabitVersions
                                     join h in db.Habits on hv.HabitId equals h.Id
                                     join he in db.HabitExecutions on hv.Id equals he.HabitVersionId
                                     where hv.Id == habitVersionId && h.UserId == userId && he.Date == date
                                     select he).AsNoTracking().FirstOrDefaultAsync();

                logger.LogInformation("Habit completed for HabitVersionId: {HabitVersionId}, Date: {Date}", habitVersionId, date);
                return execution!.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to complete habit for HabitVersionId: {HabitVersionId}, UserId: {UserId}, Date: {Date}", habitVersionId, userId, date);
                throw;
            }
        }

        public async Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, DateOnly date, string? notes)
        {
            try
            {
                logger.LogInformation("Skipping habit for UserId: {UserId}, HabitVersionId: {HabitVersionId}, Date: {Date}", userId, habitVersionId, date);

                var dayOfWeek = date.DayOfWeek.ToString();

                var affectedRows = await db.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""CompletedAt"", ""Notes"", ""PaymentProcessed"")
                    SELECT gen_random_uuid(), {habitVersionId}, {date}, {(int)App.Modules.HabitExecution.Data.HabitExecutionStatusData.Skipped}, NULL, {notes}, false
                    FROM ""HabitVersions"" hv
                    JOIN ""Habits"" h ON h.""Id"" = hv.""HabitId"" AND h.""Version"" = hv.""Version""
                    WHERE hv.""Id"" = {habitVersionId}
                      AND hv.""DaysOfWeek"" @> ARRAY[{dayOfWeek}]
                      AND h.""UserId"" = {userId}
                      AND h.""DeletedAt"" IS NULL
                      AND h.""Enabled"" = true
                      AND NOT EXISTS (
                          SELECT 1 FROM ""HabitExecutions"" he
                          WHERE he.""HabitVersionId"" = hv.""Id"" AND he.""Date"" = {date}
                      )
                ");

                if (affectedRows == 0)
                {
                    logger.LogWarning("No habit skip created - habit not found, not enabled, not scheduled for {DayOfWeek}, or already has execution for HabitVersionId: {HabitVersionId}, Date: {Date}", dayOfWeek, habitVersionId, date);
                    return new NotFoundException("Habit not found, not enabled, not scheduled for this day, or already has execution",
                      typeof(HabitExecutionPrincipal), habitVersionId.ToString());
                }

                var execution = await (from hv in db.HabitVersions
                                     join h in db.Habits on hv.HabitId equals h.Id
                                     join he in db.HabitExecutions on hv.Id equals he.HabitVersionId
                                     where hv.Id == habitVersionId && h.UserId == userId && he.Date == date
                                     select he).AsNoTracking().FirstOrDefaultAsync();

                return execution!.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to skip habit for HabitVersionId: {HabitVersionId}, UserId: {UserId}, Date: {Date}", habitVersionId, userId, date);
                throw;
            }
        }

        public async Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId,
          HabitExecutionSearch habitExecutionSearch)
        {
          try
          {
            logger.LogInformation("Searching habit executions for UserId: {UserId}, Search: {@Search}", userId, habitExecutionSearch);

            var query = (from h in db.Habits
              join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
              join he in db.HabitExecutions on hv.Id equals he.HabitVersionId
              where h.UserId == userId && h.DeletedAt == null
              select new { HabitExecution = he, HabitId = h.Id }).AsNoTracking().AsQueryable();

            if (habitExecutionSearch.Id != null)
              query = query.Where(x => x.HabitId == habitExecutionSearch.Id);

            if (habitExecutionSearch.Date != null)
              query = query.Where(x => x.HabitExecution.Date == habitExecutionSearch.Date);

            var data = await query
                .Skip(habitExecutionSearch.Skip)
                .Take(habitExecutionSearch.Limit)
                .ToListAsync();

        logger.LogInformation("Retrieved {Count} executions for UserId: {UserId}", data.Count, userId);
        return data.Select(x => x.HabitExecution.ToPrincipal()).ToList();
      }
      catch (Exception e)
      {
        logger.LogError(e, "Failed searching habit executions for UserId: {UserId}, Search: {@Search}", userId, habitExecutionSearch);
        throw;
      }
    }

    public async Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId)
    {
      try
      {
        logger.LogInformation("GetVersions for HabitId: {HabitId}, UserId: {UserId}", habitId, userId);
        var data = await (from h in db.Habits
                          join hv in db.HabitVersions on h.Id equals hv.HabitId
                          where h.Id == habitId && h.UserId == userId && h.DeletedAt == null
                          orderby hv.Version descending
                          select hv).AsNoTracking().ToListAsync();

        return data.Select(x => x.ToPrincipal()).ToList();
      }
      catch (Exception e)
      {
        logger.LogError(e, "Failed to get versions for HabitId: {HabitId}", habitId);
        throw;
      }
    }
  }
}
