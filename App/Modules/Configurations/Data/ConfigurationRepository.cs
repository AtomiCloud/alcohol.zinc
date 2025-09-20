using App.StartUp.Database;
using CSharp_Result;
using Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Configurations.Data
{
    public class ConfigurationRepository(MainDbContext db, ILogger<ConfigurationRepository> logger) : IConfigurationRepository
    {
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
                return e;
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
                return e;
            }
        }

        public async Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record)
        {
            try
            {
                logger.LogInformation("Adding Configuration for UserId: {UserId}", userId);

                var data = record.ToData(Guid.NewGuid(), userId);
                var r = db.Configurations.Add(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Configuration added with Id: {Id}", data.Id);

                return r.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Configuration for UserId: {UserId}", userId);
                return e;
            }
        }

        public async Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
        {
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

                data.Timezone = record.Timezone;
                data.DefaultCharityId = record.DefaultCharityId;
                await db.SaveChangesAsync();

                logger.LogInformation("Configuration updated for Id: {Id}", id);

                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Configuration Id: {Id}", id);
                return e;
            }
        }
    }
}
