using System.Text.RegularExpressions;
using CSharp_Result;
using Domain.Cause;
using Domain.Charity;

namespace App.Modules.Charities.Sync;

public class PledgeSyncService(
  IPledgeClient client,
  ICharityService charityService,
  ICauseService causeService,
  ILogger<PledgeSyncService> logger)
  : IPledgeSyncService
{
  public static readonly string[] SupportedCountries =
  [
    "US", "GB", "CA", "AU", "DE", "FR", "NL", "SE", "NO", "DK", "FI", "CH", "AT", "BE", "IE", "NZ", "SG", "HK", "JP", "KR",
    "IN", "ID", "CN", "BR", "MX", "AR", "CL", "CO", "PE", "ES", "IT", "PT", "PL", "CZ", "GR", "TR", "RU", "ZA", "EG", "NG",
    "KE", "TH", "MY", "PH", "VN", "AE", "SA", "IL", "UA", "RO", "HU", "BG", "HR", "SI", "SK", "LT", "LV", "EE", "IS", "LU"
  ];
  private static string BuildCauseHierarchicalName(PledgeCauseDto cause, Dictionary<int, PledgeCauseDto> causeLookup)
  {
    var names = new List<string>();
    var current = cause;
    var visited = new HashSet<int>();

    while (current != null)
    {
      if (!visited.Add(current.Id))
      {
        // Circular reference detected, break to prevent infinite loop
        break;
      }

      names.Add(current.Name);

      if (current.ParentId.HasValue && causeLookup.TryGetValue(current.ParentId.Value, out var parent))
      {
        current = parent;
      }
      else
      {
        break;
      }
    }

    names.Reverse();
    return string.Join(" > ", names);
  }

  private static string GenerateSlug(string name)
  {
    if (string.IsNullOrWhiteSpace(name)) return string.Empty;

    // Lowercase
    var slug = name.ToLowerInvariant();

    // Replace spaces with hyphens
    slug = slug.Replace(' ', '-');

    // Remove all special characters (keep only alphanumeric and hyphens)
    slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

    // Replace multiple consecutive hyphens with single hyphen
    slug = Regex.Replace(slug, @"-+", "-");

    // Trim hyphens from start and end
    slug = slug.Trim('-');

    return slug;
  }

  public async Task<Result<PledgeSyncSummary>> Sync(PledgeSyncRequest req, CancellationToken ct = default)
  {
    try
    {
      // 1) Sync Causes
      var causesRes = await client.GetCauses(ct);
      if (causesRes.IsFailure()) return causesRes.FailureOrDefault()!;
      var causes = (causesRes.SuccessOrDefault() ?? []).ToArray();

      // Build lookup dictionary for parent traversal
      var causeLookup = causes.ToDictionary(c => c.Id, c => c);

      var causesUpserted = 0;
      foreach (var c in causes)
      {
        var key = c.Id.ToString();
        var fullName = BuildCauseHierarchicalName(c, causeLookup);

        var existing = await causeService.GetByKey(key);
        if (existing.IsFailure()) return existing.FailureOrDefault()!;
        var cp = existing.SuccessOrDefault();
        if (cp == null)
        {
          var created = await causeService.Create(new CauseRecord { Key = key, Name = fullName });
          if (created.IsFailure()) return created.FailureOrDefault()!;
          causesUpserted++;
        }
        else if (!string.Equals(cp.Record.Name, fullName, StringComparison.Ordinal))
        {
          var updated = await causeService.Update(cp.Id, new CauseRecord { Key = cp.Record.Key, Name = fullName });
          if (updated.IsFailure()) return updated.FailureOrDefault()!;
          causesUpserted++;
        }
      }

      // 2) Sync Organizations: top 25 per country per cause with batching and pipelining
      var charitiesCreated = 0;
      var charitiesUpdated = 0;
      var externalLinked = 0;
      var charitiesProcessed = 0;

      // List of countries to sync (ISO 2-letter codes) - exposed for API usage too
      var countries = SupportedCountries;

      const int pageSize = 25;
      const int maxConcurrency = 1; // Process 3 country/cause combos concurrently to avoid DB overload

      // Build all country/cause combinations
      var tasks = new List<Task<(int created, int updated, int linked, int processed)>>();
      var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

      foreach (var country in countries)
      {
        foreach (var cause in causes)
        {
          var causeKey = cause.Id.ToString();
          var causeName = cause.Name;

          // Create a task for each country/cause combo with concurrency control
          var task = Task.Run(async () =>
          {
            await semaphore.WaitAsync(ct);
            try
            {
              return await ProcessCountryCauseBatch(country, causeKey, causeName, pageSize, ct);
            }
            finally
            {
              semaphore.Release();
            }
          }, ct);

          tasks.Add(task);
        }
      }

      // Wait for all tasks to complete and aggregate results
      var results = await Task.WhenAll(tasks);
      foreach (var (created, updated, linked, processed) in results)
      {
        charitiesCreated += created;
        charitiesUpdated += updated;
        externalLinked += linked;
        charitiesProcessed += processed;
      }

      return new PledgeSyncSummary(causesUpserted, charitiesCreated, charitiesUpdated, externalLinked, charitiesProcessed);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Pledge sync failed");
      throw;
    }
  }

  private async Task<(int created, int updated, int linked, int processed)> ProcessCountryCauseBatch(
    string country, string causeKey, string causeName, int pageSize, CancellationToken ct)
  {
    try
    {
      logger.LogInformation("Syncing top {Max} organizations for {Country} - {CauseName} ({CauseKey})", pageSize, country, causeName, causeKey);

      // Fetch organizations for this country/cause
      var pageRes = await client.GetOrganizations(1, pageSize, causeKey, [country], null, ct);
      if (pageRes.IsFailure())
      {
        logger.LogWarning("Failed to fetch orgs for {Country} - {CauseKey}: {Error}", country, causeKey, pageRes.FailureOrDefault()?.Message);
        return (0, 0, 0, 0);
      }

      var data = pageRes.SuccessOrDefault();
      if (data == null || data.Data.Length == 0) return (0, 0, 0, 0);

      var orgs = data.Data;
      var processed = orgs.Length;

      // Build bulk upsert request
      var bulkItems = orgs.Select<PledgeOrganizationDto, BulkUpsertCharity>(org =>
      {
        var slug = GenerateSlug(org.Name);
        var mission = !string.IsNullOrWhiteSpace(org.Mission) ? org.Mission : null;
        if (mission?.Length > 8192) mission = mission[..8192];

        return new BulkUpsertCharity
        {
          Charity = new CharityRecord
          {
            Name = org.Name,
            Slug = slug,
            Mission = mission,
            Description = null,
            Countries = !string.IsNullOrWhiteSpace(org.Country) ? [org.Country] : [],
            PrimaryRegistrationNumber = !string.IsNullOrWhiteSpace(org.NgoId) ? org.NgoId : null,
            PrimaryRegistrationCountry = !string.IsNullOrWhiteSpace(org.Country) ? org.Country : null,
            WebsiteUrl = !string.IsNullOrWhiteSpace(org.WebsiteUrl) ? org.WebsiteUrl : null,
            LogoUrl = !string.IsNullOrWhiteSpace(org.LogoUrl) ? org.LogoUrl : null,
            IsVerified = null,
            VerificationSource = null,
            LastVerifiedAt = null,
            DonationEnabled = null
          },
          ExternalId = new ExternalIdRecord
          {
            Source = "pledge",
            ExternalKey = org.Id,
            Url = !string.IsNullOrWhiteSpace(org.ProfileUrl) ? org.ProfileUrl : null,
            Payload = null,
            LastSyncedAt = DateTimeOffset.UtcNow
          },
          CauseKeys = [causeKey]
        };
      }).ToArray();

      // Single bulk upsert call
      var result = await charityService.BulkUpsert(bulkItems, ct);
      if (result.IsFailure())
      {
        logger.LogWarning("Bulk upsert failed for {Country} - {CauseKey}: {Error}", country, causeKey, result.FailureOrDefault()?.Message);
        return (0, 0, 0, processed);
      }

      var summary = result.SuccessOrDefault();
      if (summary != null)
      {
        logger.LogInformation("Completed {Country} - {CauseName}: {Created} created, {Updated} updated, {Processed} processed",
          country, causeName, summary.CharitiesCreated, summary.CharitiesUpdated, processed);

        return (summary.CharitiesCreated, summary.CharitiesUpdated, summary.ExternalIdsLinked, processed);
      }

      return (0, 0, 0, processed);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to process batch for {Country} - {CauseKey}", country, causeKey);
      return (0, 0, 0, 0);
    }
  }
}
