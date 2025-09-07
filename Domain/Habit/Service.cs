using CSharp_Result;

namespace Domain.Habit;

public class HabitService(IHabitRepository repo) : IHabitService
{
    public Task<Result<List<HabitVersionPrincipal>>> ListActiveHabits(string userId, DateOnly date)
    {
        return repo.GetActiveHabitVersions(userId, date);
    }

    public async Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId)
    {
        return await repo.GetCurrentVersion(userId, habitId);
    }

    public Task<Result<HabitPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
    {
        return repo.Create(userId, versionRecord);
    }

    public async Task<Result<HabitPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord)
    {
        // First verify the habit belongs to the user
        var habitResult = await repo.GetHabit(habitId);
        HabitPrincipal? habit = habitResult;
        
        if (habit == null || habit.UserId != userId)
        {
            return (HabitPrincipal?)null;
        }

        return await repo.Update(habitId, versionRecord);
    }

    public Task<Result<Unit?>> Delete(Guid habitId, string userId)
    {
        return repo.Delete(habitId, userId);
    }

    public Task<Result<int>> MarkDailyFailures(List<string> userIds, DateOnly date)
    {
        return repo.CreateFailedExecutions(userIds, date);
    }

    public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, DateOnly date, string? notes)
    {
        return repo.CompleteHabit(userId, habitId, date, notes);
    }

    public Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date)
    {
        return repo.GetDailyExecutions(userId, date);
    }
}
