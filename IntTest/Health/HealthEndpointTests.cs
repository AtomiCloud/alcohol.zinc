using System.Net;
using App.StartUp.Database;
using App.StartUp.Options;
using App.StartUp.Services;
using IntTest.Penalty;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IntTest.Health;

// The Kubernetes probe endpoints, wired the way Server.StartServer wires them, against a real
// Postgres (host and credentials from PENALTY_TEST_DB, database "postgres").
[Collection(DbIntegrationCollection.Name)]
public class HealthEndpointTests
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping health endpoint tests.";
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");

  private DatabaseOption Database(int? port = null)
  {
    var parts = _conn!.Split(';', StringSplitOptions.RemoveEmptyEntries)
      .Select(p => p.Split('=', 2))
      .Where(kv => kv.Length == 2)
      .ToDictionary(kv => kv[0].Trim().ToLowerInvariant(), kv => kv[1].Trim());
    return new DatabaseOption
    {
      Host = parts.GetValueOrDefault("host", "localhost"),
      Port = port ?? (int.TryParse(parts.GetValueOrDefault("port", "5432"), out var p) ? p : 5432),
      Database = "postgres",
      User = parts.GetValueOrDefault("username", parts.GetValueOrDefault("user", "postgres")),
      Password = parts.GetValueOrDefault("password", "postgres"),
      AutoMigrate = false,
      Timeout = 5
    };
  }

  private static async Task<WebApplication> Start(DatabaseOption db)
  {
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseTestServer();
    builder.Services.AddSingleton<IOptionsMonitor<Dictionary<string, DatabaseOption>>>(
      new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(new() { [MainDbContext.Key] = db }));
    builder.Services.AddDbContext<MainDbContext>();
    builder.Services.AddHealthService();
    builder.Services.AddEndpointsApiExplorer();
    // Every other endpoint needs a token here, so the probes prove they are anonymous.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer(o => o.Authority = "https://issuer.invalid");
    builder.Services.AddAuthorization(o =>
      o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    var app = builder.Build();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHealthService();
    app.MapGet("/protected", () => "secret");
    await app.StartAsync();
    return app;
  }

  private static async Task<(HttpStatusCode Status, string Body)> Get(WebApplication app, string path)
  {
    var res = await app.GetTestClient().GetAsync(path);
    return (res.StatusCode, await res.Content.ReadAsStringAsync());
  }

  [SkippableFact]
  public async Task Health_DatabaseUp_LiveAndReadyAre200WithoutAToken()
  {
    Skip.If(_conn == null, SkipReason);
    await using var app = await Start(Database());

    (await Get(app, HealthService.LivePath)).Should().Be((HttpStatusCode.OK, "Healthy"));
    (await Get(app, HealthService.ReadyPath)).Should().Be((HttpStatusCode.OK, "Healthy"));
    (await Get(app, "/protected")).Status.Should().Be(HttpStatusCode.Unauthorized, "control: other endpoints need a token");
  }

  // A new pod that cannot reach the database gets no traffic (ready 503) but is not
  // restarted (live 200). The body carries no dependency detail.
  [SkippableFact]
  public async Task Health_DatabaseUnreachable_ReadyIs503_LiveStays200()
  {
    Skip.If(_conn == null, SkipReason);
    await using var app = await Start(Database(port: 1));

    (await Get(app, HealthService.ReadyPath)).Should().Be((HttpStatusCode.ServiceUnavailable, "Unhealthy"));
    (await Get(app, HealthService.LivePath)).Should().Be((HttpStatusCode.OK, "Healthy"));
  }

  // neon and argon generate their clients from the OpenAPI document: the probes stay out of it.
  [SkippableFact]
  public async Task Health_NotInApiDescription()
  {
    Skip.If(_conn == null, SkipReason);
    await using var app = await Start(Database());

    var paths = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
      .ApiDescriptionGroups.Items.SelectMany(g => g.Items).Select(d => d.RelativePath).ToList();
    paths.Should().Contain("protected", "control: minimal endpoints are described");
    paths.Should().NotContain(p => p != null && p.StartsWith("health"));
  }
}
