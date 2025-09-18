using System.Net;
using System.Net.Http;
using System.Text;
using App.StartUp.Registry;
using App.StartUp.Services;
using App.StartUp.Services.Crm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IntTest.Crm;

public class HubSpotCrmTests
{
  private class StubHubSpotHandler : HttpMessageHandler
  {
    public List<string> Calls { get; } = [];
    public string? LatestSubscriptionBody { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.ToString() ?? string.Empty;
      Calls.Add($"{request.Method} {path}");

      if (path.Contains("crm/v3/objects/contacts/search"))
      {
        // Default: no contact found
        var content = new StringContent("{\"results\":[]}", Encoding.UTF8, "application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
      }

      if (path.Contains("crm/v3/objects/contacts/") && request.Method == HttpMethod.Patch)
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      }

      if (path.EndsWith("crm/v3/objects/contacts") && request.Method == HttpMethod.Post)
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
      }

      if (path.Contains("communication-preferences/v3/status/email/") && request.Method == HttpMethod.Put)
      {
        LatestSubscriptionBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      }

      if (path.Contains("crm/v3/objects/contacts/") && request.Method == HttpMethod.Delete)
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      }

      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
  }

  private class StubHubSpotHandlerFound : StubHubSpotHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.ToString() ?? string.Empty;
      if (path.Contains("crm/v3/objects/contacts/search"))
      {
        var content = new StringContent("{\"results\":[{\"id\":\"123\"}]}", Encoding.UTF8, "application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
      }
      return base.SendAsync(request, cancellationToken);
    }
  }

  private static ServiceProvider BuildServices(HttpMessageHandler handler)
  {
    var services = new ServiceCollection();
    services.AddLogging(c => c.AddConsole());
    services.AddHttpClient(HttpClients.HubSpot)
      .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.hubspot.local"))
      .ConfigurePrimaryHttpMessageHandler(() => handler);

    // Register CRM
    services.AddCrmService();
    services.AddOptions<App.StartUp.Options.HubSpotOption>().Configure(o =>
    {
      o.SubscriptionTypeId = 42;
      o.LegalBasis = "CONSENT_WITH_NOTICE";
      o.LegalBasisExplanation = "Test opt-in";
    });

    return services.BuildServiceProvider();
  }

  [Fact]
  public async Task Upsert_CreatesAndSubscribes_WhenNotExists()
  {
    var handler = new StubHubSpotHandler();
    using var provider = BuildServices(handler);

    var crm = provider.GetRequiredService<ICrmManagement>();
    var result = await crm.UpsertUser(new CrmUser
    {
      Email = "foo@example.com",
      FirstName = "Foo",
      LastName = "Bar",
      MarketingConsent = true
    });

    result.IsSuccess().Should().BeTrue();
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts/search"));
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts"));
    handler.Calls.Should().Contain(x => x.Contains("communication-preferences/v3/status/email/"));
    handler.LatestSubscriptionBody.Should().Contain("\"id\":42");
  }

  [Fact]
  public async Task Upsert_UpdatesAndSubscribes_WhenExists()
  {
    var handler = new StubHubSpotHandlerFound();
    using var provider = BuildServices(handler);

    var crm = provider.GetRequiredService<ICrmManagement>();
    var result = await crm.UpsertUser(new CrmUser
    {
      Email = "foo@example.com",
      FirstName = "Foo",
      LastName = "Bar",
      MarketingConsent = false
    });

    result.IsSuccess().Should().BeTrue();
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts/search"));
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts/123"));
    handler.Calls.Should().Contain(x => x.Contains("communication-preferences/v3/status/email/"));
    handler.LatestSubscriptionBody.Should().Contain("\"id\":42");
  }

  [Fact]
  public async Task RemoveUser_Deletes_WhenExists()
  {
    var handler = new StubHubSpotHandlerFound();
    using var provider = BuildServices(handler);

    var crm = provider.GetRequiredService<ICrmManagement>();
    var result = await crm.RemoveUser("foo@example.com");

    result.IsSuccess().Should().BeTrue();
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts/123"));
  }

  [Fact]
  public async Task RemoveUser_NoOp_WhenNotExists()
  {
    var handler = new StubHubSpotHandler();
    using var provider = BuildServices(handler);

    var crm = provider.GetRequiredService<ICrmManagement>();
    var result = await crm.RemoveUser("foo@example.com");

    result.IsSuccess().Should().BeTrue();
    handler.Calls.Should().Contain(x => x.Contains("crm/v3/objects/contacts/search"));
  }
}
