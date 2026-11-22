using System.Text.Json;
using CSharp_Result;
using Domain.Notification;
using Microsoft.Extensions.Logging;

namespace Domain.Charity;

public class CharityService(ICharityRepository repo, ILogger<CharityService> logger) : ICharityService
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

  public async Task<Result<Unit?>> Decommission(Guid id)
  {
    // The in-app notice asks each affected user to pick a new charity. It is written in the same
    // transaction as the decommission, so it is never lost; the dedupe key posts it once per user.
    InboxNotificationRecord Notice(string charityName) => new()
    {
      Kind = InboxKinds.CharityDecommissioned,
      Title = "Pick a new charity",
      Body = $"{charityName} is no longer taking donations. Pick a new charity for the habits that give to it.",
      Data = JsonSerializer.Serialize(new { charityId = id }),
      DedupeKey = $"{InboxKinds.CharityDecommissioned}:{id}",
    };

    var result = await repo.Decommission(id, Notice);
    if (!result.IsSuccess()) return result.FailureOrDefault()!;
    if (result.Get() is not { } done) return (Unit?)null;

    logger.LogInformation(
      "Charity {Id} decommissioned: {Held} charges put on hold, {Notices} users notified",
      id, done.HeldPenalties, done.NoticesPosted);
    return new Unit();
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
