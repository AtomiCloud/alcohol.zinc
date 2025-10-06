using CSharp_Result;

namespace Domain.Charity
{
    public interface ICharityRepository
    {
        Task<Result<Charity?>> Get(Guid id);
        Task<Result<IEnumerable<CharityPrincipal>>> Search(CharitySearch search);
        Task<Result<CharityPrincipal>> Create(CharityRecord model);
        Task<Result<CharityPrincipal?>> Update(Guid id, CharityRecord record);
        Task<Result<Unit?>> Delete(Guid id);

        // Linking
        Task<Result<Unit?>> SetCauses(Guid id, IEnumerable<string> causeKeys);
        Task<Result<Unit?>> AddCause(Guid id, string causeKey);

        // External Ids
        Task<Result<CharityPrincipal?>> GetByExternalId(string source, string externalKey);
        Task<Result<Unit>> UpsertExternalId(Guid charityId, ExternalIdRecord external);

        // Bulk Operations
        Task<Result<BulkUpsertResult>> BulkUpsert(IEnumerable<BulkUpsertCharity> charities, CancellationToken ct = default);
    }
}
