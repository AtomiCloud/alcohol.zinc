using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Routine.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Modules.Habit.Data
{
    public class HabitRepository(MainDbContext db, ILogger<HabitRepository> logger) : IHabitRepository
    {
        // A habit version's charity is the one reference the client chooses, so a missing charity
        // is a bad request, not a server error.
        private const string CharityForeignKey = "FK_HabitVersions_Charities_CharityId";

        private static bool IsUnknownCharity(ReferenceConstraintException e) =>
            e.InnerException is PostgresException { ConstraintName: CharityForeignKey };

        private InvalidRequestException UnknownCharity(Guid? charityId)
        {
            // The failed inserts would otherwise be retried by the next SaveChanges on this context.
            db.ChangeTracker.Clear();
            return new InvalidRequestException("CharityId", $"Charity {charityId} does not exist");
        }

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
                                 where h.UserId == userId
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

                // The user's habit order; Id breaks ties so pages never overlap.
                var data = await query
                    .OrderBy(x => x.Habit.SortOrder)
                    .ThenBy(x => x.Habit.Id)
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

        public async Task<Result<List<HabitPrincipal>>> GetHabits(List<Guid> habitIds)
        {
            try
            {
                var data = await db.Habits
                    .AsNoTracking()
                    .Where(x => habitIds.Contains(x.Id) && x.DeletedAt == null)
                    .ToListAsync();
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving {Count} habits", habitIds.Count);
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

        public async Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord, Guid? routineId)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                logger.LogInformation("Creating habit for UserId: {UserId}, Task: {Task}, RoutineId: {RoutineId}",
                    userId, versionRecord.Task, routineId);

                Guid resolvedRoutineId;
                if (routineId != null)
                {
                    // A supplied routine must be the user's own, live routine
                    var owned = await db.Routines
                        .AnyAsync(r => r.Id == routineId && r.UserId == userId && r.DeletedAt == null);
                    if (!owned)
                    {
                        logger.LogWarning("Routine {RoutineId} not found for UserId: {UserId}", routineId, userId);
                        await transaction.RollbackAsync();
                        return new NotFoundException("Routine Not Found", typeof(Domain.Routine.RoutinePrincipal),
                            routineId.Value.ToString());
                    }
                    resolvedRoutineId = routineId.Value;
                }
                else
                {
                    // No routine supplied: the habit gets its own one-habit routine
                    var routineData = new Domain.Routine.RoutineRecord { Name = versionRecord.Task }.ToData(userId);
                    db.Routines.Add(routineData);
                    resolvedRoutineId = routineData.Id;
                }

                // A new habit goes to the bottom of the user's list
                var lastSortOrder = await db.Habits
                    .Where(h => h.UserId == userId && h.DeletedAt == null)
                    .MaxAsync(h => (int?)h.SortOrder);

                // Create main habit record with version 1
                var habitData = new HabitData
                {
                    UserId = userId,
                    Version = 1,
                    Enabled = true,  // New habits are enabled by default
                    RoutineId = resolvedRoutineId,
                    SortOrder = lastSortOrder + 1 ?? 0
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
            catch (ReferenceConstraintException e) when (IsUnknownCharity(e))
            {
                logger.LogWarning("Create habit refused for UserId: {UserId}: unknown CharityId {CharityId}",
                    userId, versionRecord.CharityId);
                await transaction.RollbackAsync();
                return UnknownCharity(versionRecord.CharityId);
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
            catch (ReferenceConstraintException e) when (IsUnknownCharity(e))
            {
                logger.LogWarning("Update habit refused for HabitId: {HabitId}: unknown CharityId {CharityId}",
                    habitId, versionRecord.CharityId);
                await transaction.RollbackAsync();
                return UnknownCharity(versionRecord.CharityId);
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

                // Habit deletion is a soft delete, so the NfcTags FK cascade never
                // fires — release the physical tag here or its id stays claimed
                // forever (409 for other users, 404 for the owner).
                await db.NfcTags.Where(x => x.HabitId == habitId).ExecuteDeleteAsync();

                // A routine lives as long as it has a live habit: soft delete the
                // habit's routine when this was its last non-deleted habit. One
                // statement, so the emptiness check and the update see the same state.
                await db.Database.ExecuteSqlAsync($@"
                    UPDATE ""Routines"" r SET ""DeletedAt"" = {DateTime.UtcNow}
                    WHERE r.""Id"" = (SELECT h.""RoutineId"" FROM ""Habits"" h WHERE h.""Id"" = {habitId})
                      AND r.""DeletedAt"" IS NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM ""Habits"" live
                          WHERE live.""RoutineId"" = r.""Id"" AND live.""DeletedAt"" IS NULL
                      )
                ");

                logger.LogInformation("Soft deleted habit for HabitId: {HabitId}", habitId);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete habit for HabitId: {HabitId}, UserId: {UserId}", habitId, userId);
                throw;
            }
        }

        public async Task<Result<List<Guid>>> GetOrderedIds(string userId)
        {
            try
            {
                return await db.Habits
                    .AsNoTracking()
                    .Where(h => h.UserId == userId && h.DeletedAt == null)
                    .OrderBy(h => h.SortOrder)
                    .ThenBy(h => h.Id)
                    .Select(h => h.Id)
                    .ToListAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to read habit order for UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<Result<Unit>> SetOrder(string userId, IReadOnlyList<Guid> orderedIds)
        {
            try
            {
                logger.LogInformation("Setting order of {Count} habits for UserId: {UserId}", orderedIds.Count, userId);
                var ids = orderedIds.ToArray();
                // One statement, so the whole order is written or none of it. Ids that are not the
                // user's live habits match no row.
                await db.Database.ExecuteSqlAsync($@"
                    UPDATE ""Habits"" h SET ""SortOrder"" = o.idx - 1
                    FROM unnest({ids}::uuid[]) WITH ORDINALITY AS o(id, idx)
                    WHERE h.""Id"" = o.id AND h.""UserId"" = {userId} AND h.""DeletedAt"" IS NULL
                ");
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to set habit order for UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<Result<string?>> GetTaskNameByExecutionId(Guid executionId)
        {
            try
            {
                return await (from e in db.HabitExecutions
                              join hv in db.HabitVersions on e.HabitVersionId equals hv.Id
                              where e.Id == executionId
                              select hv.Task).AsNoTracking().FirstOrDefaultAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to get task name for execution: {ExecutionId}", executionId);
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

    public async Task<Result<HabitVersionPrincipal?>> GetVersion(Guid habitVersionId)
    {
      try
      {
        var data = await db.HabitVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == habitVersionId);
        return data?.ToPrincipal();
      }
      catch (Exception e)
      {
        logger.LogError(e, "Failed to get habit version {HabitVersionId}", habitVersionId);
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
