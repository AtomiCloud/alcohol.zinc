using App.StartUp.Options;
using Domain.Subscription;
using Microsoft.Extensions.Options;

namespace App.Modules.Subscription;

// Daily subscription maintenance: charges due renewals (rolling periods, moving
// failures into grace, cancelling lapsed/cancel-requested rows), then retries any
// stale Konnect mirrors. Idempotency keys make a cross-replica double tick
// charge-safe; this in-process gate just avoids overlapping work.
public class SubscriptionRenewalHostedService(
  IServiceProvider serviceProvider,
  IOptionsMonitor<SubscriptionOption> options,
  ILogger<SubscriptionRenewalHostedService> logger
) : IHostedService, IDisposable
{
  private Timer? _timer;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (!options.CurrentValue.RenewalEnabled)
    {
      logger.LogInformation(
        "SubscriptionRenewalHostedService disabled (Subscription.RenewalEnabled=false); not scheduling");
      return Task.CompletedTask;
    }

    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(24));
    logger.LogInformation("SubscriptionRenewalHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    if (!await _gate.WaitAsync(0))
    {
      logger.LogInformation("Subscription renewal previous pass still running; skipping tick");
      return;
    }
    try
    {
      using var scope = serviceProvider.CreateScope();
      var svc = scope.ServiceProvider.GetRequiredService<ISubscriptionManagementService>();

      var renewals = await svc.ProcessRenewals(DateTime.UtcNow, 500);
      if (renewals.IsSuccess())
        logger.LogInformation("Subscription renewal settled {Count} subscription(s)", (int)renewals);
      else
        logger.LogError(renewals.FailureOrDefault(), "Subscription renewal error");

      var mirrored = await svc.ProcessKonnectMirror(200);
      if (mirrored.IsSuccess())
        logger.LogInformation("Konnect mirror synced {Count} subscription(s)", (int)mirrored);
      else
        logger.LogError(mirrored.FailureOrDefault(), "Konnect mirror error");
    }
    catch (Exception e)
    {
      logger.LogError(e, "SubscriptionRenewalHostedService failed");
    }
    finally
    {
      try
      {
        _gate.Release();
      }
      catch (ObjectDisposedException)
      {
        // Host shutdown disposed the gate while this pass was in flight;
        // nothing left to release.
      }
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    logger.LogInformation("SubscriptionRenewalHostedService stopping");
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
    _gate.Dispose();
  }
}
