// using App.StartUp.Database;
// using CSharp_Result;
// using Domain.Stats;
// using Microsoft.EntityFrameworkCore;

// namespace App.Modules.Stats.Data
// {
//     public class StatsRepository(MainDbContext db, ILogger<StatsRepository> logger) : IStatsRepository
//     {
//         public async Task<Result<StatsModel>> Get(string sub, DateOnly date)
//         {
//             try
//             {
//                 logger.LogInformation("Retrieving Stats by Sub: {Sub}, Date: {Date}", sub, date);
//                 var data = await db.Stats.AsNoTracking().FirstOrDefaultAsync(x => x.Sub == sub && x.Date == date);
//                 if (data == null)
//                 {
//                     logger.LogWarning("Stats not found for Sub: {Sub}, Date: {Date}", sub, date);
//                     return (StatsModel?)null;
//                 }
//                 return data.ToDomain();
//             }
//             catch (Exception e)
//             {
//                 logger.LogError(e, "Failed retrieving Stats by Sub: {Sub}, Date: {Date}", sub, date);
//                 return e;
//             }
//         }

//         public async Task<Result<List<StatsModel>>> GetBySubAsync(string sub)
//         {
//             try
//             {
//                 logger.LogInformation("Retrieving Stats for Sub: {Sub}", sub);
//                 var data = await db.Stats.AsNoTracking().Where(x => x.Sub == sub).ToListAsync();
//                 logger.LogInformation("Retrieved {Count} Stats for Sub: {Sub}", data.Count, sub);
//                 return data.Select(x => x.ToDomain()).ToList();
//             }
//             catch (Exception e)
//             {
//                 logger.LogError(e, "Failed retrieving Stats for Sub: {Sub}", sub);
//                 return e;
//             }
//         }

//         public async Task<Result<StatsModel>> Create(StatsModel model)
//         {
//             try
//             {
//                 logger.LogInformation("Adding Stats for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 var data = model.ToData();
//                 var r = db.Stats.Add(data);
//                 await db.SaveChangesAsync();
//                 logger.LogInformation("Stats added for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 return r.Entity.ToDomain();
//             }
//             catch (Exception e)
//             {
//                 logger.LogError(e, "Failed to create Stats for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 return e;
//             }
//         }

//         public async Task<Result<StatsModel?>> Update(StatsModel model)
//         {
//             try
//             {
//                 logger.LogInformation("Updating Stats for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 var data = await db.Stats.FirstOrDefaultAsync(x => x.Sub == model.Sub && x.Date == model.Date);
//                 if (data == null)
//                 {
//                     logger.LogWarning("Stats not found for update, Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                     return (StatsModel?)null;
//                 }

//                 data.AmountForDev = model.AmountForDev;
//                 data.AmountForCharity = model.AmountForCharity;
//                 var updated = db.Stats.Update(data);
//                 await db.SaveChangesAsync();
//                 logger.LogInformation("Stats updated for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 return updated.Entity.ToDomain();
//             }
//             catch (Exception e)
//             {
//                 logger.LogError(e, "Failed to update Stats for Sub: {Sub}, Date: {Date}", model.Sub, model.Date);
//                 return e;
//             }
//         }

//         public async Task<Result<Unit?>> Delete(string sub, DateOnly date)
//         {
//             try
//             {
//                 logger.LogInformation("Deleting Stats for Sub: {Sub}, Date: {Date}", sub, date);
//                 var data = await db.Stats.FirstOrDefaultAsync(x => x.Sub == sub && x.Date == date);
//                 if (data == null)
//                 {
//                     logger.LogWarning("Stats not found for delete, Sub: {Sub}, Date: {Date}", sub, date);
//                     return (Unit?)null;
//                 }

//                 db.Stats.Remove(data);
//                 await db.SaveChangesAsync();
//                 logger.LogInformation("Stats deleted for Sub: {Sub}, Date: {Date}", sub, date);
//                 return new Unit();
//             }
//             catch (Exception e)
//             {
//                 logger.LogError(e, "Failed to delete Stats for Sub: {Sub}, Date: {Date}", sub, date);
//                 return e;
//             }
//         }
//     }
// }
