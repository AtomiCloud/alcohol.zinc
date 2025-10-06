using App.Error.V1;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Cause;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Causes.Data
{
    public class CauseRepository(MainDbContext db, ILogger<CauseRepository> logger) : ICauseRepository
    {
        public async Task<Result<Cause?>> Get(Guid id)
        {
            try
            {
                logger.LogInformation("Retrieving Cause by Id: {Id}", id);
                var data = await db
                    .Causes
                    .Where(x => x.Id == id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Cause not found for Id: {Id}", id);
                    return (Cause?)null;
                }
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Cause by Id: {Id}", id);
                return e;
            }
        }

        public async Task<Result<IEnumerable<CausePrincipal>>> Search(CauseSearch search)
        {
            try
            {
                logger.LogInformation("Searching Causes with '{@Search}'", search);
                var query = db.Causes.AsQueryable();
                if (!string.IsNullOrWhiteSpace(search.Key))
                    query = query.Where(x => EF.Functions.ILike(x.Key, $"%{search.Key}%"));
                if (!string.IsNullOrWhiteSpace(search.Name))
                    query = query.Where(x => EF.Functions.ILike(x.Name, $"%{search.Name}%"));

                var list = await query
                    .Skip(search.Skip)
                    .Take(search.Limit)
                    .ToListAsync();
                return list.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed searching Causes with '{@Search}'", search);
                throw;
            }
        }

        public async Task<Result<CausePrincipal>> Create(CauseRecord model)
        {
            try
            {
                logger.LogInformation("Adding Cause: {Key}", model.Key);
                var data = model.ToData();
                var r = db.Causes.Add(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Cause added with Id: {Id}", data.Id);
                return r.Entity.ToPrincipal();
            }
            catch (UniqueConstraintException e)
            {
                logger.LogError(e, "Cause create conflict for Key: {Key}", model.Key);
                return new EntityConflict("Cause already exists", typeof(CausePrincipal))
                    .ToException();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Cause: {Key}", model.Key);
                throw;
            }
        }

        public async Task<Result<CausePrincipal?>> Update(Guid id, CauseRecord record)
        {
            try
            {
                logger.LogInformation("Updating Cause Id: {Id}", id);
                var data = await db
                    .Causes
                    .Where(x => x.Id == id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Cause not found for update, Id: {Id}", id);
                    return (CausePrincipal?)null;
                }

                // Only Name is mutable; Key is immutable once created
                data = data.ToData(record);
                var updated = db.Causes.Update(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Cause updated for Id: {Id}", id);
                return updated.Entity.ToPrincipal();
            }
            catch (UniqueConstraintException e)
            {
                logger.LogError(e, "Cause update conflict for Id: {Id}", id);
                return new EntityConflict("Cause update conflicts with existing record", typeof(CausePrincipal))
                    .ToException();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Cause Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<Unit?>> Delete(Guid id)
        {
            try
            {
                logger.LogInformation("Deleting Cause Id: {Id}", id);
                var data = await db.Causes.FirstOrDefaultAsync(x => x.Id == id);
                if (data == null)
                {
                    logger.LogWarning("Cause not found for delete, Id: {Id}", id);
                    return (Unit?)null;
                }
                db.Causes.Remove(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Cause deleted for Id: {Id}", id);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Cause Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<CausePrincipal?>> GetByKey(string key)
        {
            try
            {
                logger.LogInformation("Retrieving Cause by Key: {Key}", key);
                var data = await db
                    .Causes
                    .Where(x => x.Key == key)
                    .FirstOrDefaultAsync();
                return data?.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Cause by Key: {Key}", key);
                throw;
            }
        }
    }
}
