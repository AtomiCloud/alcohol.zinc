using CSharp_Result;

namespace Domain.Habit
{
    public interface IHabitRepository
    {
        Task<Result<List<HabitPrincipal>>> List(string userId);
        Task<Result<HabitPrincipal?>> Get(string userId, Guid id);
        Task<Result<Habit?>> GetWithRelations(string userId, Guid id);
        Task<Result<HabitPrincipal>> Create(HabitPrincipal principal);
        Task<Result<HabitPrincipal?>> Update(HabitPrincipal principal);
        Task<Result<Unit?>> Delete(Guid id, string userId);
    }
}
