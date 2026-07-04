using System.Net;
using System.Net.Http.Json;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Subscription;

namespace App.Modules.Subscription.Konnect;

// IKonnectGateway over the Kong Konnect Metering & Billing (OpenMeter) v3 API.
// Mirror-only: every failure is returned as a Result failure for the caller to
// log — the local table + config remain the source of truth. Paths are relative
// to the KONNECT HttpClient BaseAddress (https://{region}.api.konghq.com); the
// bearer token comes from HttpClient config (BearerAuth).
public class KonnectGateway(IHttpClientFactory httpClientFactory, ILogger<KonnectGateway> logger)
  : IKonnectGateway
{
  private const string Base = "v3/openmeter";

  private HttpClient Client => httpClientFactory.CreateClient(HttpClients.Konnect);

  public async Task<Result<string>> UpsertCustomer(string userId)
  {
    try
    {
      // filter[...] syntax is required: bare params (?key=...) are silently
      // IGNORED by the API and return an unfiltered page — FirstOrDefault would
      // then pick an arbitrary customer and corrupt the mirror. Verified live.
      var found = await this.Client.GetFromJsonAsync<KonnectPage<KonnectCustomer>>(
        $"{Base}/customers?filter%5Bkey%5D={Uri.EscapeDataString(userId)}");
      if (found?.Data.FirstOrDefault(c => c.Key == userId) is { } existing) return existing.Id;

      var res = await this.Client.PostAsJsonAsync($"{Base}/customers", new KonnectCreateCustomerReq
      {
        Key = userId,
        Name = userId,
        UsageAttribution = new KonnectUsageAttribution { SubjectKeys = [userId] }
      });

      // Lost a create race: another writer inserted the same key; re-read it.
      if (res.StatusCode == HttpStatusCode.Conflict)
      {
        var refetch = await this.Client.GetFromJsonAsync<KonnectPage<KonnectCustomer>>(
          $"{Base}/customers?filter%5Bkey%5D={Uri.EscapeDataString(userId)}");
        if (refetch?.Data.FirstOrDefault(c => c.Key == userId) is { } raced) return raced.Id;
        return new Exception($"Konnect customer create conflicted but key {userId} not found on re-read");
      }

      res.EnsureSuccessStatusCode();
      var created = await res.Content.ReadFromJsonAsync<KonnectCustomer>();
      if (created is null) return new Exception("Konnect customer create returned an empty body");
      return created.Id;
    }
    catch (Exception e)
    {
      logger.LogWarning(e, "Konnect UpsertCustomer failed for {UserId}", userId);
      return e;
    }
  }

  public async Task<Result<Unit>> UpsertSubscription(
    string konnectCustomerId, string tier, DateTime periodStart, DateTime periodEnd, string status)
  {
    try
    {
      // Resolve the target plan id so an already-correct mirror is a no-op.
      // (filter[...] syntax — bare params are ignored and return everything.)
      var plans = await this.Client.GetFromJsonAsync<KonnectPage<KonnectPlan>>(
        $"{Base}/plans?filter%5Bkey%5D={Uri.EscapeDataString(tier)}");
      var targetPlan = plans?.Data.FirstOrDefault(p => p.Key == tier && p.Status is "active" or null);
      if (targetPlan is null)
        return new Exception($"Konnect plan '{tier}' not found; create it per docs/KONNECT_SETUP.md");

      var subs = await this.Client.GetFromJsonAsync<KonnectPage<KonnectSubscription>>(
        $"{Base}/subscriptions?filter%5Bcustomer_id%5D={Uri.EscapeDataString(konnectCustomerId)}");
      var current = subs?.Data.FirstOrDefault(s =>
        s.CustomerId == konnectCustomerId && s.Status is "active" or "scheduled");

      // zinc's row status rides along as a label for reporting.
      var labels = new Dictionary<string, string> { ["zinc_status"] = status };

      if (current is null)
      {
        var createRes = await this.Client.PostAsJsonAsync($"{Base}/subscriptions",
          new KonnectCreateSubscriptionReq
          {
            Customer = new KonnectRef { Id = konnectCustomerId },
            Plan = new KonnectPlanRef { Key = tier },
            Labels = labels
          });
        createRes.EnsureSuccessStatusCode();
        return new Unit();
      }

      if (current.PlanId == targetPlan.Id) return new Unit(); // mirror already correct

      var changeRes = await this.Client.PostAsJsonAsync($"{Base}/subscriptions/{current.Id}/change",
        new KonnectChangeSubscriptionReq
        {
          Customer = new KonnectRef { Id = konnectCustomerId },
          Plan = new KonnectPlanRef { Key = tier },
          Timing = "immediate",
          Labels = labels
        });
      changeRes.EnsureSuccessStatusCode();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogWarning(e, "Konnect UpsertSubscription failed for customer {CustomerId} tier {Tier}",
        konnectCustomerId, tier);
      return e;
    }
  }
}
