using System.Transactions;
using App.Error.V1;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Configuration;
using Domain.Exceptions;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Configurations.Data
{
    public class ConfigurationRepository(MainDbContext db, ILogger<ConfigurationRepository> logger) : IConfigurationRepository
    {
        // A switched-off (deleted) charity cannot be newly chosen as the default charity.
        // Reads the charity row FOR SHARE, so the lock is held until the caller's transaction ends and a
        // concurrent CharityRepository.Delete (FOR UPDATE) waits for this choice, or this waits for it.
        private async Task<bool> IsSwitchedOffCharity(Guid? charityId)
        {
            if (charityId is not { } id) return false;
            var deletedAt = await db.Database
                .SqlQuery<DateTime?>($@"SELECT ""DeletedAt"" AS ""Value"" FROM ""Charities"" WHERE ""Id"" = {id} FOR SHARE")
                .ToListAsync();
            return deletedAt is [not null];
        }

        private static InvalidRequestException SwitchedOffCharity(Guid? charityId) =>
            new("DefaultCharityId", $"Charity {charityId} is no longer available");

        public async Task<Result<Configuration?>> GetByUserId(string userId)
        {
            try
            {
                logger.LogInformation("Retrieving Configuration by UserId: {UserId}", userId);

                var data = await db.Configurations
                    .AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .Include(x => x.Charity)
                    .FirstOrDefaultAsync();

                if (data == null)
                {
                    logger.LogWarning("Configuration not found for UserId: {UserId}", userId);
                    return (Configuration?)null;
                }

                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Configuration by UserId: {UserId}", userId);
                return e;
            }
        }
        public async Task<Result<Configuration?>> Get(Guid id)
        {
            try
            {
                logger.LogInformation("Retrieving Configuration by Id: {Id}", id);

                var data = await db.Configurations
                    .AsNoTracking()
                    .Where(x => x.Id == id)
                    .Include(x => x.Charity)
                    .FirstOrDefaultAsync();
                
                if (data == null)
                {
                    logger.LogWarning("Configuration not found for Id: {Id}", id);
                    return (Configuration?)null;
                }
                
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Configuration by Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<Configuration?>> Get(Guid id, string userId)
        {
            try
            {
                logger.LogInformation("Retrieving Configuration by Id: {Id} and UserId: {UserId}", id, userId);

                var data = await db.Configurations
                    .AsNoTracking()
                    .Where(x => x.Id == id && x.UserId == userId)
                    .Include(x => x.Charity)
                    .FirstOrDefaultAsync();
                
                if (data == null)
                {
                    logger.LogWarning("Configuration not found for Id: {Id} and UserId: {UserId}", id, userId);
                    return (Configuration?)null;
                }
                
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Configuration by UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record)
        {
            // The charity check reads the charity row FOR SHARE; it must share a transaction with the write so
            // a concurrent charity Delete cannot slip in between. Own transaction only when the caller has none.
            await using var ownTx = Transaction.Current == null && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync()
                : null;
            try
            {
                logger.LogInformation("Adding Configuration for UserId: {UserId}", userId);

                if (await IsSwitchedOffCharity(record.DefaultCharityId))
                    return SwitchedOffCharity(record.DefaultCharityId);

                var data = record.ToData(Guid.NewGuid(), userId);
                var r = db.Configurations.Add(data);
                await db.SaveChangesAsync();
                if (ownTx != null) await ownTx.CommitAsync();

                logger.LogInformation("Configuration added with Id: {Id}", data.Id);

                return r.Entity.ToPrincipal();
            }
            catch (UniqueConstraintException)
            {
                // One configuration per user: a second create (e.g. two first sign-ins at once) conflicts.
                logger.LogWarning("Configuration already exists for UserId: {UserId}", userId);
                db.ChangeTracker.Clear();
                return new EntityConflict("Configuration already exists for this user", typeof(ConfigurationPrincipal))
                    .ToException();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Configuration for UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
        {
            // The charity check reads the charity row FOR SHARE; it must share a transaction with the write so
            // a concurrent charity Delete cannot slip in between. Own transaction only when the caller has none.
            await using var ownTx = Transaction.Current == null && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync()
                : null;
            try
            { 
                logger.LogInformation("Updating Configuration Id: {Id}, UserId: {UserId}", id, userId);

                var data = await db
                    .Configurations
                    .Where(x => x.Id == id && x.UserId == userId)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Configuration not found for update, Id: {Id}, UserId: {UserId}", id, userId);
                    return (ConfigurationPrincipal?)null;
                }

                if (record.DefaultCharityId != data.DefaultCharityId && await IsSwitchedOffCharity(record.DefaultCharityId))
                    return SwitchedOffCharity(record.DefaultCharityId);

                data.Timezone = record.Timezone;
                data.DefaultCharityId = record.DefaultCharityId;
                await db.SaveChangesAsync();
                if (ownTx != null) await ownTx.CommitAsync();

                logger.LogInformation("Configuration updated for Id: {Id}", id);

                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Configuration Id: {Id}", id);
                throw;
            }
        }
    }
}
