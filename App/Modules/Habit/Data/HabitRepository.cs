using App.StartUp.Database;
using CSharp_Result;
using Domain.Habit;
using Domain.User;
using Domain.Charity;
using Microsoft.EntityFrameworkCore;
using App.Modules.Users.Data;
using App.Modules.Charities.Data;

namespace App.Modules.Habit.Data
{
    public class HabitRepository(MainDbContext db, ILogger<HabitRepository> logger) : IHabitRepository
    {
        public async Task<Result<List<HabitPrincipal>>> List(string userId)
        {
            try
            {
                logger.LogInformation("Retrieving HabitPrincipals for UserId: {UserId}", userId);
                var data = await db.Habits.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
                logger.LogInformation("Retrieved {Count} HabitPrincipals for UserId: {UserId}", data.Count, userId);
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving HabitPrincipals for UserId: {UserId}", userId);
                return e;
            }
        }

        public async Task<Result<HabitPrincipal?>> Get(string userId, Guid id)
        {
            try
            {
                logger.LogInformation("Retrieving HabitPrincipal by Id: {Id}, UserId: {UserId}", id, userId);
                var data = await db.Habits.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
                if (data == null)
                {
                    logger.LogWarning("HabitPrincipal not found for Id: {Id}, UserId: {UserId}", id, userId);
                    return (HabitPrincipal?)null;
                }
                return data.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving HabitPrincipal by Id: {Id}, UserId: {UserId}", id, userId);
                return e;
            }
        }

        public async Task<Result<Habit?>> GetWithRelations(string userId, Guid id)
        {
            try
            {
                logger.LogInformation("Retrieving Habit with relations by Id: {Id}, UserId: {UserId}", id, userId);
                var habitData = await db.Habits.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
                if (habitData == null)
                {
                    logger.LogWarning("Habit not found for Id: {Id}, UserId: {UserId}", id, userId);
                    return (Habit?)null;
                }

                var userData = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
                if (userData == null)
                {
                    logger.LogWarning("User not found for UserId: {UserId}", userId);
                    return (Habit?)null;
                }

                var charityData = habitData.CharityId.HasValue 
                    ? await db.Charities.FirstOrDefaultAsync(x => x.Id == habitData.CharityId.Value)
                    : null;

                var habitPrincipal = habitData.ToPrincipal();
                var userPrincipal = userData.ToPrincipal();
                var charity = charityData?.ToDomain();

                return habitPrincipal.ToHabit(userPrincipal, charity);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Habit with relations by Id: {Id}, UserId: {UserId}", id, userId);
                return e;
            }
        }

        public async Task<Result<HabitPrincipal>> Create(HabitPrincipal principal)
        {
            try
            {
                logger.LogInformation("Adding HabitPrincipal for UserId: {UserId}, Task: {Task}", principal.UserId, principal.Record.Task);
                var data = principal.ToData();
                var r = db.Habits.Add(data);
                await db.SaveChangesAsync();
                logger.LogInformation("HabitPrincipal added with Id: {Id} for UserId: {UserId}", data.Id, data.UserId);
                return r.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create HabitPrincipal for UserId: {UserId}, Task: {Task}", principal.UserId, principal.Record.Task);
                return e;
            }
        }

        public async Task<Result<HabitPrincipal?>> Update(HabitPrincipal principal)
        {
            try
            {
                logger.LogInformation("Updating HabitPrincipal Id: {Id}, UserId: {UserId}", principal.Id, principal.UserId);
                var data = await db.Habits.FirstOrDefaultAsync(x => x.Id == principal.Id && x.UserId == principal.UserId);
                if (data == null)
                {
                    logger.LogWarning("HabitPrincipal not found for update, Id: {Id}, UserId: {UserId}", principal.Id, principal.UserId);
                    return (HabitPrincipal?)null;
                }

                data.Task = principal.Record.Task;
                data.DayOfWeek = principal.Record.DayOfWeek;
                data.NotificationTime = principal.Record.NotificationTime;
                data.StakeCents = (int)(principal.Record.Stake.Amount / principal.Record.Stake.Currency.MinimalAmount);
                data.RatioBasisPoints = (int)(principal.Record.Ratio * 1000m);
                data.StartDate = principal.Record.StartDate;
                data.EndDate = principal.Record.EndDate;
                data.CharityId = principal.CharityId;
                data.Version = principal.Record.Version;
                data.HabitId = principal.HabitId;

                var updated = db.Habits.Update(data);
                await db.SaveChangesAsync();
                logger.LogInformation("HabitPrincipal updated for Id: {Id}, UserId: {UserId}", principal.Id, principal.UserId);
                return updated.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update HabitPrincipal Id: {Id}, UserId: {UserId}", principal.Id, principal.UserId);
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(Guid id, string userId)
        {
            try
            {
                logger.LogInformation("Deleting HabitPrincipal Id: {Id}, UserId: {UserId}", id, userId);
                var data = await db.Habits.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
                if (data == null)
                {
                    logger.LogWarning("HabitPrincipal not found for delete, Id: {Id}, UserId: {UserId}", id, userId);
                    return (Unit?)null;
                }

                db.Habits.Remove(data);
                await db.SaveChangesAsync();
                logger.LogInformation("HabitPrincipal deleted for Id: {Id}, UserId: {UserId}", id, userId);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete HabitPrincipal Id: {Id}, UserId: {UserId}", id, userId);
                return e;
            }
        }
    }
}
