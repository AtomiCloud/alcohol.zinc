using CSharp_Result;

namespace Domain.Habit;

public interface IHabitService
{
  Task<Result<List<HabitPrincipal>>> List(string userId);
  Task<Result<HabitPrincipal?>> Get(string userId, Guid id);
  Task<Result<Habit?>> GetWithRelations(string userId, Guid id);
  Task<Result<HabitPrincipal>> Create(string userId, HabitRecord habitRecord, int? charityId);
  Task<Result<HabitPrincipal?>> Update(HabitPrincipal habitPrincipal);
  Task<Result<Unit?>> Delete(Guid id, string userId);
}
