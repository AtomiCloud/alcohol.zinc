using CSharp_Result;
using Domain.Routine;

namespace App.Modules.Routine;

public class RoutineService(IRoutineRepository repo) : IRoutineService
{
  public Task<Result<RoutinePrincipal?>> Get(Guid id) => repo.Get(id);

  public Task<Result<RoutinePrincipal?>> Get(Guid id, string? userId) => repo.Get(id, userId);

  public Task<Result<RoutinePrincipal>> Create(string userId, RoutineRecord record) => repo.Create(userId, record);

  public Task<Result<RoutinePrincipal?>> Update(Guid id, RoutineRecord record) => repo.Update(id, record);

  public Task<Result<Unit?>> Delete(Guid id, string userId) => repo.Delete(id, userId);

  public Task<Result<List<RoutinePrincipal>>> Search(RoutineSearch search) => repo.Search(search);
}
