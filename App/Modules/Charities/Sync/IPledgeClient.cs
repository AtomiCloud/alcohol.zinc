using CSharp_Result;

namespace App.Modules.Charities.Sync;

public interface IPledgeClient
{
  Task<Result<IEnumerable<PledgeCauseDto>>> GetCauses(CancellationToken ct = default);
  Task<Result<PledgeOrganizationsPage>> GetOrganizations(int page, int perPage, string? causeKey = null, string[]? countries = null, DateTimeOffset? updatedSince = null, CancellationToken ct = default);
}
