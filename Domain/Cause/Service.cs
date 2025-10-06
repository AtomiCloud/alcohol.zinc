using CSharp_Result;

namespace Domain.Cause;

public class CauseService(ICauseRepository repo) : ICauseService
{
  public Task<Result<Cause?>> Get(Guid id)
  {
    return repo.Get(id);
  }

  public Task<Result<IEnumerable<CausePrincipal>>> Search(CauseSearch search)
  {
    return repo.Search(search);
  }

  public Task<Result<CausePrincipal>> Create(CauseRecord record)
  {
    return repo.Create(record);
  }

  public Task<Result<CausePrincipal?>> Update(Guid id, CauseRecord record)
  {
    return repo.Update(id, record);
  }

  public Task<Result<Unit?>> Delete(Guid id)
  {
    return repo.Delete(id);
  }

  public Task<Result<CausePrincipal?>> GetByKey(string key)
  {
    return repo.GetByKey(key);
  }
}
