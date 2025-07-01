using App.StartUp.Database;
using CSharp_Result;
using Domain.Charity;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Charities.Data
{
    public class CharityRepository(MainDbContext db, ILogger<CharityRepository> logger) : ICharityRepository
    {
        public async Task<Result<CharityModel?>> Get(int id)
        {
            try
            {
                logger.LogInformation("Retrieving Charity by Id: {Id}", id);

                var data = await db
                    .Charities
                    .Where(x => x.Id == id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Charity not found for Id: {Id}", id);

                    return (CharityModel?)null;
                }
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Charity by Id: {Id}", id);

                return e;
            }
        }

        public async Task<Result<List<CharityModel>>> GetAll()
        {
            try
            {
                logger.LogInformation("Retrieving all Charities");

                var data = await db.Charities.ToListAsync();
                return data.Select(x => x.ToDomain()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving all Charities");

                return e;
            }
        }

        public async Task<Result<CharityModel>> Create(CharityModel model)
        {
            try
            {
                logger.LogInformation("Adding Charity: {Name}", model.Name);

                var data = model.ToData();
                var r = db.Charities.Add(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity added with Id: {Id}", data.Id);

                return r.Entity.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Charity: {Name}", model.Name);

                return e;
            }
        }

        public async Task<Result<CharityModel?>> Update(CharityModel model)
        {
            try
            {
                logger.LogInformation("Updating Charity Id: {Id}", model.Id);

                var data = await db
                    .Charities
                    .Where(x => x.Id == model.Id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Charity not found for update, Id: {Id}", model.Id);

                    return (CharityModel?)null;
                }
                data.Name = model.Name;
                data.Email = model.Email;
                var updated = db.Charities.Update(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity updated for Id: {Id}", model.Id);

                return updated.Entity.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Charity Id: {Id}", model.Id);
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(int id)
        {
            try
            {
                logger.LogInformation("Deleting Charity Id: {Id}", id);

                var data = await db.Charities.FirstOrDefaultAsync(x => x.Id == id);
                if (data == null)
                {
                    logger.LogWarning("Charity not found for delete, Id: {Id}", id);
                    return (Unit?)null;
                }
                db.Charities.Remove(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity deleted for Id: {Id}", id);

                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Charity Id: {Id}", id);
                
                return e;
            }
        }
    }
}
