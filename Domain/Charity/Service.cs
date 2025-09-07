using CSharp_Result;

namespace Domain.Charity;

public class CharityService(ICharityRepository repo) : ICharityService
{
  public Task<Result<Charity?>> GetById(Guid id)
  {
    return repo.Get(id);
  }

  public Task<Result<IEnumerable<CharityPrincipal>>> GetAll()
  {
    return repo.GetAll();
  }

  public Task<Result<CharityPrincipal>> Create(CharityRecord record)
  {
    return repo.Create(record);
  }

  public Task<Result<CharityPrincipal?>> Update(Guid id, CharityRecord record)
  {
    var principal = new CharityPrincipal
    {
      Id = id,
      Record = record
    };
    return repo.Update(principal);
  }

  public Task<Result<Unit?>> Delete(Guid id)
  {
    return repo.Delete(id);
  }
}