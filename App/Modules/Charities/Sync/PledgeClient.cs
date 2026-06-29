using System.Net.Http.Json;
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

      if (countries != null && countries.Length > 0)
      {
        url += $"&country={Uri.EscapeDataString(countries[0])}";
      }

      var res = await this.Client.GetFromJsonAsync<PledgeOrganizationsPage>(url, cancellationToken: ct);
      logger.LogInformation("Fetched Pledge organizations page {Page} for cause {Cause}: {Count} results", page, causeKey ?? "all", res?.Data.Length ?? 0);
      return res ?? new PledgeOrganizationsPage { Data = [], Page = page, PerPage = perPage, TotalCount = 0 };
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to fetch Pledge organizations page {Page} for cause {Cause}", page, causeKey ?? "all");
      throw;
    }
  }

  public async Task<Result<PledgeDonationDto>> CreateDonation(PledgeDonationReq req, CancellationToken ct = default)
  {
    try
    {
      if (this.Client.BaseAddress == null)
      {
        logger.LogWarning("Pledge HttpClient is not configured (BaseAddress missing)");
        return new ValidationError("Pledge HttpClient not configured",
          new Dictionary<string, string[]> { ["httpClient"] = [HttpClients.Pledge] }).ToException();
      }

      using var response = await this.Client.PostAsJsonAsync("v1/donations", req, ct);
      var body = await response.Content.ReadAsStringAsync(ct);
      if (!response.IsSuccessStatusCode)
      {
        // 4xx/5xx -> failed Result (not thrown). The caller records it and retries; one bad
        // donation must not abort the payout batch.
        logger.LogError("Pledge CreateDonation failed: {Status} org={Org} body={Body}",
          (int)response.StatusCode, req.OrganizationId, body);
        return new ValidationError($"Pledge donation failed with status {(int)response.StatusCode}",
          new Dictionary<string, string[]> { ["pledge"] = [body] }).ToException();
      }

      var dto = body.ToObj<PledgeDonationDto>();
      if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
      {
        logger.LogError("Pledge CreateDonation returned an unparseable/empty body: {Body}", body);
        return new ValidationError("Pledge donation response missing id",
          new Dictionary<string, string[]> { ["pledge"] = [body] }).ToException();
      }
      logger.LogInformation("Pledge donation created: {DonationId} (status {Status}) for org {Org}", dto.Id, dto.Status, req.OrganizationId);
      return dto;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to create Pledge donation for org {Org}", req.OrganizationId);
      throw;
    }
  }

  public async Task<Result<PledgeDonationsPage>> GetDonations(int page, CancellationToken ct = default)
  {
    try
    {
      if (this.Client.BaseAddress == null)
      {
        logger.LogWarning("Pledge HttpClient is not configured (BaseAddress missing)");
        return new ValidationError("Pledge HttpClient not configured",
          new Dictionary<string, string[]> { ["httpClient"] = [HttpClients.Pledge] }).ToException();
      }
      var res = await this.Client.GetFromJsonAsync<PledgeDonationsPage>($"v1/donations?page={page}", cancellationToken: ct);
      return res ?? new PledgeDonationsPage { Results = [], Page = page };
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to fetch Pledge donations page {Page}", page);
      throw;
    }
  }
}
