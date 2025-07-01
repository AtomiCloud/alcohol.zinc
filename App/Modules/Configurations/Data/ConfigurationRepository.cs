using App.StartUp.Database;
using CSharp_Result;
using Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Configurations.Data
{
    public class ConfigurationRepository(MainDbContext db, ILogger<ConfigurationRepository> logger) : IConfigurationRepository
    {
        public async Task<Result<ConfigurationModel?>> Get(string sub)
        {
            try
            {
                logger.LogInformation("Retrieving Config by Sub: {Sub}", sub);

                var data = await db
                    .Configurations
                    .Where(x => x.Sub == sub)
                    .FirstOrDefaultAsync();

                return data?.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Config by Sub: {Sub}", sub);
                return e;
            }
        }

        public async Task<Result<ConfigurationModel>> Create(ConfigurationModel model)
        {
            try
            {
                logger.LogInformation("Adding Config for Sub: {Sub}", model.Sub);

                var data = model.ToData();
                db.Configurations.Add(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Config added for Sub: {Sub}", model.Sub);
                return model;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to add Config for Sub: {Sub}", model.Sub);
                return e;
            }
        }

        public async Task<Result<ConfigurationModel?>> Update(ConfigurationModel model)
        {
            try
            {
                logger.LogInformation("Updating Config for Sub: {Sub}", model.Sub);

                var data = await db
                    .Configurations
                    .Where(x => x.Sub == model.Sub)
                    .FirstOrDefaultAsync();

                if (data == null)
                {
                    logger.LogWarning("Config not found for update, Sub: {Sub}", model.Sub);
                    return (ConfigurationModel?)null;
                }

                data.Timezone = model.Timezone;
                data.EndOfDay = model.EndOfDay;
                data.DefaultCharityId = model.DefaultCharityId;
                await db.SaveChangesAsync();
                logger.LogInformation("Config updated for Sub: {Sub}", model.Sub);
                return model;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Config for Sub: {Sub}", model.Sub);
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(string sub)
        {
            try
            {
                logger.LogInformation("Deleting Config for Sub: {Sub}", sub);

                var data = await db
                    .Configurations
                    .Where(x => x.Sub == sub)
                    .FirstOrDefaultAsync();

                if (data == null)
                {
                    logger.LogWarning("Config not found for delete, Sub: {Sub}", sub);
                    return (Unit?)null;
                }

                db.Configurations.Remove(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Config deleted for Sub: {Sub}", sub);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Config for Sub: {Sub}", sub);
                return e;
            }
        }
    }
}
