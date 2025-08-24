using CSharp_Result;

namespace Domain.Habit;

public class HabitService(IHabitRepository repo) : IHabitService
{
    public Task<Result<List<HabitPrincipal>>> List(string userId)
    {
        return repo.List(userId);
    }

    public Task<Result<HabitPrincipal?>> Get(string userId, Guid id)
    {
        return repo.Get(userId, id);
    }

    public Task<Result<Habit?>> GetWithRelations(string userId, Guid id)
    {
        return repo.GetWithRelations(userId, id);
    }

    public Task<Result<HabitPrincipal>> Create(string userId, HabitRecord habitRecord, int? charityId)
    {
        // Create HabitPrincipal from HabitRecord
        var habitPrincipal = new HabitPrincipal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HabitId = Guid.NewGuid(), // Generate unique Guid for HabitId
            CharityId = charityId,
            Record = habitRecord
        };
        
        return repo.Create(habitPrincipal);
    }

    public Task<Result<HabitPrincipal?>> Update(HabitPrincipal habitPrincipal)
    {
        return repo.Update(habitPrincipal);
    }

    public Task<Result<Unit?>> Delete(Guid id, string userId)
    {
        return repo.Delete(id, userId);
    }
}
