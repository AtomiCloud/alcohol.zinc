using App.StartUp.Database;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace App.StartUp.Services;

// Probe endpoints for Kubernetes. Anonymous, version-neutral, left out of the OpenAPI
// document, and the body is only "Healthy" or "Unhealthy" (no dependency detail).
public static class HealthService
{
  public const string LivePath = "/health/live";
  public const string ReadyPath = "/health/ready";
  private const string ReadyTag = "ready";

  public static IServiceCollection AddHealthService(this IServiceCollection services)
  {
    services.AddHealthChecks()
      .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(2));
    return services;
  }

  public static WebApplication MapHealthService(this WebApplication app)
  {
    // Live: the process serves HTTP. No dependency checks, so a database blip never
    // restarts pods.
    app.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false })
      .AllowAnonymous()
      .ExcludeFromDescription();
    // Ready: only send traffic to a pod that can reach the database.
    app.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) })
      .AllowAnonymous()
      .ExcludeFromDescription();
    return app;
  }
}

public class DatabaseHealthCheck(MainDbContext db) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
    CancellationToken cancellationToken = default)
  {
    try
    {
      return await db.Database.CanConnectAsync(cancellationToken)
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy();
    }
    catch (OperationCanceledException)
    {
      // The registration's 2 s timeout fired.
      return HealthCheckResult.Unhealthy();
    }
  }
}
