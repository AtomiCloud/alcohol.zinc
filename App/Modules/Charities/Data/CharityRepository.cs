using App.StartUp.Database;
using CSharp_Result;
using Domain.Charity;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Charities.Data
{
    public class CharityRepository(MainDbContext db, ILogger<CharityRepository> logger) : ICharityRepository
    {
        public async Task<Result<Charity?>> Get(Guid id)
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

                    return (Charity?)null;
                }
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Charity by Id: {Id}", id);

                return e;
            }
        }

        public async Task<Result<IEnumerable<CharityPrincipal>>> GetAll()
        {
            try
            {
                logger.LogInformation("Retrieving all Charities");

                var data = await db.Charities.ToListAsync();
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving all Charities");

                return e;
            }
        }

        public async Task<Result<CharityPrincipal>> Create(CharityRecord model)
        {
            try
            {
                logger.LogInformation("Adding Charity: {Name}", model.Name);

                var data = model.ToData();
                var r = db.Charities.Add(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity added with Id: {Id}", data.Id);

                return r.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Charity: {Name}", model.Name);

                return e;
            }
        }

        public async Task<Result<CharityPrincipal?>> Update(CharityPrincipal principal)
        {
            try
            {
                logger.LogInformation("Updating Charity Id: {Id}", principal.Id);

                var data = await db
                    .Charities
                    .Where(x => x.Id == principal.Id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Charity not found for update, Id: {Id}", principal.Id);

                    return (CharityPrincipal?)null;
                }
                data.Name = principal.Record.Name;
                data.Email = principal.Record.Email;
                data.Address = principal.Record.Address;
                var updated = db.Charities.Update(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity updated for Id: {Id}", principal.Id);

                return updated.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Charity Id: {Id}", principal.Id);
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(Guid id)
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
