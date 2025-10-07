using CSharp_Result;

namespace Domain.Charity;

public class CharityService(ICharityRepository repo) : ICharityService
{
  public Task<Result<Charity?>> Get(Guid id)
  {
    return repo.Get(id);
  }

  public Task<Result<IEnumerable<CharityPrincipal>>> Search(CharitySearch search)
  {
    return repo.Search(search);
  }

  public Task<Result<CharityPrincipal>> Create(CharityRecord record)
  {
    return repo.Create(record);
  }

  public Task<Result<CharityPrincipal?>> Update(Guid id, CharityRecord record)
  {
    return repo.Update(id, record);
  }

  public Task<Result<Unit?>> Delete(Guid id)
  {
    return repo.Delete(id);
  }

  public Task<Result<Unit?>> SetCauses(Guid id, IEnumerable<string> causeKeys)
  {
    return repo.SetCauses(id, causeKeys);
  }

  public Task<Result<Unit?>> AddCause(Guid id, string causeKey)
  {
    return repo.AddCause(id, causeKey);
  }

  public Task<Result<CharityPrincipal?>> GetByExternalId(string source, string externalKey)
  {
    return repo.GetByExternalId(source, externalKey);
  }

  public Task<Result<Unit>> UpsertExternalId(Guid charityId, ExternalIdRecord external)
  {
    return repo.UpsertExternalId(charityId, external);
  }

  public Task<Result<BulkUpsertResult>> BulkUpsert(IEnumerable<BulkUpsertCharity> charities, CancellationToken ct = default)
  {
    return repo.BulkUpsert(charities, ct);
  }
}
