using CSharp_Result;

namespace App.Modules.Charities.Sync;

public interface IPledgeSyncService
{
  Task<Result<PledgeSyncSummary>> Sync(CancellationToken ct = default);
}


public record PledgeSyncSummary(int CausesUpserted, int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CharitiesProcessed);

