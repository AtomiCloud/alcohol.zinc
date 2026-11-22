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

        // Decommission: hidden like Delete, and no more money flows to the charity. Queued charges not yet
        // taken from a card are put on hold, later missed days are recorded on hold, and payouts to it stop.
        // Every record is kept. Null when the charity does not exist. Repeating it is a harmless success.
        // `notice(charityName)` is posted to each affected user's in-app inbox IN THE SAME TRANSACTION, so a
        // committed decommission always has its notices (once per user: its DedupeKey is honoured).
        Task<Result<CharityDecommissionResult?>> Decommission(Guid id, Func<string, Notification.InboxNotificationRecord> notice);

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
