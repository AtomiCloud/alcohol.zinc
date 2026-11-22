using System.Text.Json;
using CSharp_Result;
using Domain.Notification;
using Microsoft.Extensions.Logging;

namespace Domain.Charity;

public class CharityService(ICharityRepository repo, IInboxService inbox, ILogger<CharityService> logger) : ICharityService
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
    var result = await repo.Decommission(id);
    if (!result.IsSuccess()) return result.FailureOrDefault()!;
    if (result.Get() is not { } done) return (Unit?)null;

    logger.LogInformation(
      "Charity {Id} decommissioned: {Held} queued charges put on hold, {Users} users to tell",
      id, done.HeldPenalties, done.AffectedUserIds.Count);

    // Tell each affected user in the app. Best effort: the decommission is already committed, and the
    // dedupe key makes a repeat decommission (e.g. a retry after a failed send) post at most once each.
    var record = new InboxNotificationRecord
    {
      Kind = InboxKinds.CharityDecommissioned,
      Title = "Pick a new charity",
      Body = $"{done.CharityName} is no longer taking donations. Pick a new charity for the habits that give to it.",
      Data = JsonSerializer.Serialize(new { charityId = id }),
      DedupeKey = $"{InboxKinds.CharityDecommissioned}:{id}",
    };
    foreach (var userId in done.AffectedUserIds)
    {
      try
      {
        var posted = await inbox.Post(userId, record);
        if (!posted.IsSuccess())
          logger.LogWarning(posted.FailureOrDefault(), "Decommission notice failed for charity {Id}", id);
      }
      catch (Exception e)
      {
        logger.LogWarning(e, "Decommission notice failed for charity {Id}", id);
      }
    }

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
