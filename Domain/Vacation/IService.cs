using CSharp_Result;

namespace Domain.Vacation;

public interface IVacationService
{
  Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record);
  Task<Result<List<VacationPrincipal>>> Search(VacationSearch search);
  Task<Result<Unit?>> Delete(string userId, Guid vacationId);
  Task<Result<VacationPrincipal?>> EndToday(string userId, Guid vacationId);
}

