using CSharp_Result;

namespace Domain.Habit;

public class HabitService(IHabitRepository repo) : IHabitService
{
    public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
    {
        return repo.SearchHabits(habitSearch);
    }

    public Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId)
      => repo.GetCurrentVersion(userId, habitId);

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

        return await repo.Update(habitId, userId, versionRecord, enabled);
    }

    public Task<Result<Unit?>> Delete(Guid habitId, string userId)
    {
        return repo.Delete(habitId, userId);
    }

    public Task<Result<int>> MarkDailyFailures(List<string> userIds, DateOnly date)
    {
        return repo.CreateFailedExecutions(userIds, date);
    }

    // public Task<Result<DateOnly>> GetUserCurrentDate(string userId)
    // {
    //     return repo.GetUserCurrentDate(userId);
    // }

    public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, string? notes)
    {
        return repo
          .GetUserCurrentDate(userId, habitVersionId)
          .ThenAwait(date => repo.CompleteHabit(userId, habitVersionId, date, notes));
    }

    public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, 
      HabitExecutionSearch habitExecutionSearch)
    {
        return repo.SearchHabitExecutions(userId, habitExecutionSearch);
    }
}
