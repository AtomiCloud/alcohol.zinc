using App.StartUp.Database;
using CSharp_Result;
using Domain.Subscription;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Subscription.Data
{
    public class SubscriptionEventRepository(MainDbContext db, ILogger<SubscriptionEventRepository> logger)
        : ISubscriptionEventRepository
    {
        public async Task<Result<SubscriptionEventPrincipal>> Append(SubscriptionEventRecord record)
        {
            try
            {
                var data = record.ToData();
                db.SubscriptionEvents.Add(data);
                await db.SaveChangesAsync();
                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Append subscription event failed for UserId={UserId}, EventType={EventType}",
                    record.UserId, record.EventType);
                throw;
            }
        }

        public async Task<Result<List<SubscriptionEventPrincipal>>> ListByUser(string userId, int limit)
        {
            try
            {
                var rows = await db.SubscriptionEvents.AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.OccurredAt)
                    .ThenByDescending(x => x.Id)
                    .Take(limit)
                    .ToListAsync();
                return rows.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "ListByUser subscription events failed for UserId={UserId}", userId);
                throw;
            }
        }
    }
}
