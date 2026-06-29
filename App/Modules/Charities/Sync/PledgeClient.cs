using System.Net.Http.Json;
using App.Error.V1;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Domain.Disbursement;

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
      var status = (int)response.StatusCode;
      if (!response.IsSuccessStatusCode)
      {
        // Do NOT log or surface the response body: this request carries the donor's name/email and
        // provider validation errors echo that input, so the body is PII and must not land in logs
        // or the (durable) LastError column. Log the status only.
        // 4xx => the provider refused the request and created NOTHING, so it is safe to release the
        // penalties for retry (DonationRejectedException). 5xx => the donation MAY have been created
        // before the error, so this is AMBIGUOUS: return a plain failure and let the caller keep the
        // disbursement Pending for reconciliation rather than risk a double payout.
        if (status is >= 400 and < 500)
        {
          logger.LogWarning("Pledge CreateDonation rejected (status {Status}) for org {Org}", status, req.OrganizationId);
          return new DonationRejectedException($"Pledge donation rejected with status {status}");
        }
        logger.LogError("Pledge CreateDonation failed (status {Status}, ambiguous) for org {Org}", status, req.OrganizationId);
        return new ValidationError($"Pledge donation failed with status {status}",
          new Dictionary<string, string[]> { ["pledge"] = [$"status {status}"] }).ToException();
      }

      var body = await response.Content.ReadAsStringAsync(ct);
      var dto = body.ToObj<PledgeDonationDto>();
      if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
      {
        // 2xx but no usable id: we cannot confirm the donation id, yet it may have been created.
        // Treat as ambiguous (not a rejection) so reconciliation settles it by metadata.
        logger.LogError("Pledge CreateDonation returned {Status} with no usable donation id for org {Org}", status, req.OrganizationId);
        return new ValidationError("Pledge donation response missing id",
          new Dictionary<string, string[]> { ["pledge"] = ["missing id"] }).ToException();
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
      // A null body is an unknown vendor state, NOT an empty page. Returning an empty page here
      // would let reconciliation read "no such donation" and release penalties for re-donation;
      // surface a failure so the caller treats the lookup as inconclusive and keeps it Pending.
      if (res == null)
        return new ValidationError("Pledge donations response was empty",
          new Dictionary<string, string[]> { ["pledge"] = ["null body"] }).ToException();
      return res;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to fetch Pledge donations page {Page}", page);
      throw;
    }
  }
}
