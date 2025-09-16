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

        public async Task<Result<ConfigurationPrincipal?>> Update(ConfigurationPrincipal principal)
        {
            try
            {
                logger.LogInformation("Updating Configuration Id: {Id}", principal.Id);

                var data = await db
                    .Configurations
                    .Where(x => x.Id == principal.Id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Configuration not found for update, Id: {Id}", principal.Id);
                    return (ConfigurationPrincipal?)null;
                }

                data.Timezone = principal.Record.Timezone;
                data.EndOfDay = principal.Record.EndOfDay;
                data.DefaultCharityId = principal.Record.DefaultCharityId;
                var updated = db.Configurations.Update(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Configuration updated for Id: {Id}", principal.Id);

                return updated.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Configuration Id: {Id}", principal.Id);
                return e;
            }
        }
    }
}
