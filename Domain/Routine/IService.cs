using CSharp_Result;

namespace Domain.Routine;

public interface IRoutineService
{
  Task<Result<RoutinePrincipal?>> Get(Guid id);
  Task<Result<RoutinePrincipal?>> Get(Guid id, string? userId);
  Task<Result<RoutinePrincipal>> Create(string userId, RoutineRecord record);
  Task<Result<RoutinePrincipal?>> Update(Guid id, RoutineRecord record);
  Task<Result<Unit?>> Delete(Guid id, string userId);
  Task<Result<List<RoutinePrincipal>>> Search(RoutineSearch search);
}
