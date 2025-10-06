using App.Error.V1;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;

namespace App.Modules.Charities.Sync;

  public class PledgeClient(IHttpClientFactory httpClientFactory, ILogger<PledgeClient> logger) : IPledgeClient
{
  private HttpClient Client => httpClientFactory.CreateClient(HttpClients.Pledge);

  public async Task<Result<IEnumerable<PledgeCauseDto>>> GetCauses(CancellationToken ct = default)
  {
    try
    {
      if (this.Client.BaseAddress == null)
      {
        logger.LogWarning("Pledge HttpClient is not configured (BaseAddress missing)");
        return new ValidationError("Pledge HttpClient not configured",
          new Dictionary<string, string[]> { ["httpClient"] = [HttpClients.Pledge] }).ToException();
      }
      var res = await this.Client.GetFromJsonAsync<PledgeCausesRes>("v1/causes", cancellationToken: ct);

      return res?.Causes ?? [];
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to fetch Pledge causes");
      throw;
    }
  }

  public async Task<Result<PledgeOrganizationsPage>> GetOrganizations(int page, int perPage, string? causeKey = null, string[]? countries = null, DateTimeOffset? updatedSince = null, CancellationToken ct = default)
  {
    try
    {
      if (this.Client.BaseAddress == null)
      {
        logger.LogWarning("Pledge HttpClient is not configured (BaseAddress missing)");
        return new ValidationError("Pledge HttpClient not configured", new Dictionary<string, string[]> { ["httpClient"] = [HttpClients.Pledge] }).ToException();
      }

      var url = $"v1/organizations?page={page}&per_page={perPage}";
      if (!string.IsNullOrWhiteSpace(causeKey))
      {
        url += $"&cause_id={Uri.EscapeDataString(causeKey)}";
      }
      if (updatedSince != null)
      {
        var iso = updatedSince.Value.UtcDateTime.ToString("o");
        url += $"&updated_since={Uri.EscapeDataString(iso)}";
      }
      if (countries is { Length: > 0 })
      {
        url = countries.Aggregate(url, (current, c) => current + $"&country={Uri.EscapeDataString(c)}");
      }

      var res = await this.Client.GetFromJsonAsync<PledgeOrganizationsPage>(url, cancellationToken: ct);
      logger.LogInformation("Fetched Pledge organizations page {Page} for cause {Cause}: {Count} results", page, causeKey ?? "all", res?.Data.Length ?? 0);
      return res ?? new PledgeOrganizationsPage { Data = [], Page = page, PerPage = perPage, TotalPages = 0 };
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to fetch Pledge organizations page {Page} for cause {Cause}", page, causeKey ?? "all");
      throw;
    }
  }
}
