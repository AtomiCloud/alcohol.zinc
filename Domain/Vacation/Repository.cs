using CSharp_Result;

namespace Domain.Vacation;

public interface IVacationRepository
{
  Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record);
  Task<Result<VacationPrincipal?>> Update(Guid id, VacationRecord record);
  Task<Result<Unit?>> Delete(Guid id, string userId);
  Task<Result<List<VacationPrincipal>>> Search(VacationSearch search);
  Task<Result<List<VacationPrincipal>>> ListActiveForUserOnDate(string userId, DateOnly date);
}

