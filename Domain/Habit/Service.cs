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

    public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
    {
        return repo.Create(userId, versionRecord);
    }

    public async Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord, bool enabled)
    {
        // First verify the habit belongs to the user
        var habitResult = await repo.GetHabit(habitId);
        HabitPrincipal? habit = habitResult;

        if (habit == null || habit.UserId != userId)
        {
            return (HabitVersionPrincipal?)null;
        }

        return await repo.Update(habitId, versionRecord, enabled);
    }

    public Task<Result<Unit?>> Delete(Guid habitId, string userId)
    {
        return repo.Delete(habitId, userId);
    }

    public Task<Result<int>> MarkDailyFailures(List<string> userIds, DateOnly date)
    {
        return repo.CreateFailedExecutions(userIds, date);
    }

    public async Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, string? notes)
    {
        // Get user's timezone from configuration to determine today's date
        var currentDate = await repo.GetUserCurrentDate(userId);
        return await repo.CompleteHabit(userId, habitId, currentDate, notes);
    }

    public Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date)
    {
        return repo.GetDailyExecutions(userId, date);
    }
}
