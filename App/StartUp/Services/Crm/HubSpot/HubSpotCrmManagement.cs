using System.Text;
using App.StartUp.Options;
using App.StartUp.Registry;
using App.StartUp.Services.Crm.HubSpot.Models;
using App.Utility;
using CSharp_Result;
using Microsoft.Extensions.Options;

namespace App.StartUp.Services.Crm.HubSpot;

public class HubSpotCrmManagement(
  IHttpClientFactory httpClientsFactory,
  IOptions<HubSpotOption> hubSpotOptions,
  ILogger<ICrmManagement> logger
) : ICrmManagement
{
  private HttpClient HttpClient => httpClientsFactory.CreateClient(HttpClients.HubSpot);
  private HubSpotOption Opt => hubSpotOptions.Value;

  private async Task<Result<string?>> FindContactIdByEmail(string email)
  {
    try
    {
      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = new Uri("crm/v3/objects/contacts/search", UriKind.Relative),
        Content = new StringContent(
          new HubSpotSearchRequest(
            [new HubSpotSearchFilterGroup([new HubSpotSearchFilter("email", "EQ", email)])],
            ["id"]
          ).ToJson(),
          Encoding.UTF8,
          "application/json")
      };
      using var res = await HttpClient.SendAsync(request);
      if (!res.IsSuccessStatusCode)
      {
        var body = await res.Content.ReadAsStringAsync();
        logger.LogWarning("HubSpot search failed (status={Status}): {Body}", (int)res.StatusCode, body);
        return (string?)null; // Not found or failed; treat as not found
      }

      var json = await res.Content.ReadAsStringAsync();
      var parsed = json.ToObj<HubSpotSearchResponse>();
      var id = parsed?.Results.FirstOrDefault()?.Id;
      return id ?? (string?)null;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed searching HubSpot contact by email: {Email}", email);
      throw;
    }
  }

  private async Task<Result<Unit>> ManageConsentByEmail(string email, bool consent)
  {
    try
    {
      logger.LogInformation("Updating HubSpot subscription status for {Email}: consent={Consent}", email, consent);
      if (Opt.SubscriptionTypeId <= 0)
      {
        throw new ApplicationException("HubSpot.SubscriptionTypeId must be configured (> 0)");
      }

      var update = new HubSpotSubscriptionStatusUpdate
      {
        Id = Opt.SubscriptionTypeId,
        Subscribed = consent,
        LegalBasis = Opt.LegalBasis,
        LegalBasisExplanation = Opt.LegalBasisExplanation
      };
      var reqModel = new HubSpotUpdateSubscriptionByEmailRequest
      {
        SubscriptionStatuses = [ update ]
      };

      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Put,
        RequestUri = new Uri($"communication-preferences/v3/status/email/{Uri.EscapeDataString(email)}", UriKind.Relative),
        Content = new StringContent(reqModel.ToJson(), Encoding.UTF8, "application/json")
      };
      using var res = await HttpClient.SendAsync(request);
      res.EnsureSuccessStatusCode();
      logger.LogInformation("Updated HubSpot subscription status for {Email}", email);
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to update HubSpot subscription status for {Email}", email);
      throw;
    }
  }

  public async Task<Result<Unit>> UpsertUser(CrmUser user)
  {
    try
    {
      logger.LogInformation("Upserting HubSpot contact: {Email}", user.Email);
      var existingId = await FindContactIdByEmail(user.Email);
      var props = new Dictionary<string, object?>
      {
        { "email", user.Email },
      };
      if (!string.IsNullOrWhiteSpace(user.FirstName)) props["firstname"] = user.FirstName;
      if (!string.IsNullOrWhiteSpace(user.LastName)) props["lastname"] = user.LastName;

      if (!string.IsNullOrEmpty(existingId))
      {
        var patch = new HttpRequestMessage
        {
          Method = HttpMethod.Patch,
          RequestUri = new Uri($"crm/v3/objects/contacts/{existingId}", UriKind.Relative),
          Content = new StringContent(new HubSpotCreateUpdateRequest { Properties = props }.ToJson(), Encoding.UTF8,
            "application/json")
        };

        using var resp = await HttpClient.SendAsync(patch);
        resp.EnsureSuccessStatusCode();
        logger.LogInformation("Updated HubSpot contact: {Email} (id={Id})", user.Email, existingId);
        return await ManageConsentByEmail(user.Email, user.MarketingConsent);
      }
      else
      {
        var post = new HttpRequestMessage
        {
          Method = HttpMethod.Post,
          RequestUri = new Uri("crm/v3/objects/contacts", UriKind.Relative),
          Content = new StringContent(new HubSpotCreateUpdateRequest { Properties = props }.ToJson(), Encoding.UTF8,
            "application/json")
        };

        using var resp = await HttpClient.SendAsync(post);
        resp.EnsureSuccessStatusCode();
        logger.LogInformation("Created HubSpot contact: {Email}", user.Email);
        return await ManageConsentByEmail(user.Email, user.MarketingConsent);
      }
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to upsert HubSpot contact: {Email}", user.Email);
      throw;
    }
  }

  public async Task<Result<Unit>> RemoveUser(string email)
  {
    try
    {
      logger.LogInformation("Removing HubSpot contact: {Email}", email);
      var id = await FindContactIdByEmail(email);
      if (string.IsNullOrEmpty(id))
      {
        logger.LogInformation("HubSpot contact not found; nothing to remove: {Email}", email);
        return new Unit();
      }

      var del = new HttpRequestMessage
      {
        Method = HttpMethod.Delete,
        RequestUri = new Uri($"crm/v3/objects/contacts/{id}", UriKind.Relative),
      };

      using var resp = await HttpClient.SendAsync(del);
      resp.EnsureSuccessStatusCode();
      logger.LogInformation("Removed HubSpot contact: {Email} (id={Id})", email, id);
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to remove HubSpot contact: {Email}", email);
      throw;
    }
  }
}
