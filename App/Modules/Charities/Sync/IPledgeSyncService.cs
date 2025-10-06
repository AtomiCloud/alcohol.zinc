using CSharp_Result;

namespace App.Modules.Charities.Sync;

public interface IPledgeSyncService
{
  Task<Result<PledgeSyncSummary>> Sync(PledgeSyncRequest req, CancellationToken ct = default);
}

public record PledgeSyncRequest(int MaxPages = 1, int PageSize = 100, string[]? Countries = null, DateTimeOffset? UpdatedSince = null);

public record PledgeSyncSummary(int CausesUpserted, int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CharitiesProcessed);

