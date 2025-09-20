using CSharp_Result;

namespace Domain.Charity;

public interface ICharityService
{
  Task<Result<Charity?>> GetById(Guid id);
  Task<Result<IEnumerable<CharityPrincipal>>> GetAll();

  Task<Result<CharityPrincipal>> Create(CharityRecord record);
  Task<Result<CharityPrincipal?>> Update(Guid id, CharityRecord record);

  Task<Result<Unit?>> Delete(Guid id);
}