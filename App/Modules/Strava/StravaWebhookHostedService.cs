using App.StartUp.Options;
using Domain.Strava;
using Microsoft.Extensions.Options;

namespace App.Modules.Strava;

// Drains the Strava webhook inbox: fetches each activity and routes it to the strava proof
// plugin. A failed event stays claimed until its lease runs out, then is retried; after
// MaxAttempts it is dropped.
public class StravaWebhookHostedService(
  IServiceProvider serviceProvider,
  IOptionsMonitor<StravaOption> options,
  ILogger<StravaWebhookHostedService> logger
) : IHostedService, IDisposable
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
  private Timer? _timer;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (!options.CurrentValue.Enabled)
    {
      logger.LogInformation("StravaWebhookHostedService disabled (Strava.Enabled=false); not scheduling");
      return Task.CompletedTask;
    }

    // Runs are usually synced within a minute of finishing; 20 s keeps the habit feeling instant.
    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(20));
    logger.LogInformation("StravaWebhookHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    if (!await _gate.WaitAsync(0)) return;
    try
    {
      using var scope = serviceProvider.CreateScope();
      var inbox = scope.ServiceProvider.GetRequiredService<IStravaInboxRepository>();
      var claimed = await inbox.Claim(batchSize: 50, Lease);
      if (claimed.IsFailure())
      {
        logger.LogError(claimed.FailureOrDefault(), "Strava inbox claim failed");
        return;
      }

      foreach (var item in claimed.Get())
        await ProcessOne(item, inbox, scope.ServiceProvider.GetRequiredService<IStravaService>());
    }
    catch (Exception e)
    {
      logger.LogError(e, "StravaWebhookHostedService failed");
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task ProcessOne(StravaInboxItem item, IStravaInboxRepository inbox, IStravaService service)
  {
    var maxAttempts = options.CurrentValue.MaxAttempts;
    try
    {
      var processed = await service.Process(item.Event);
      if (processed.IsSuccess() || item.Attempts >= maxAttempts)
      {
        if (processed.IsFailure())
          logger.LogError(processed.FailureOrDefault(),
            "Dropping Strava event {Id} for ObjectId={ObjectId} after {Attempts} attempts",
            item.Id, item.Event.ObjectId, item.Attempts);
        await inbox.Delete(item.Id);
      }
      else
        logger.LogWarning(processed.FailureOrDefault(), "Strava event {Id} failed (attempt {Attempts}); will retry",
          item.Id, item.Attempts);
    }
    catch (Exception e)
    {
      // One bad event must not stop the batch; it retries after its lease.
      logger.LogError(e, "Strava event {Id} threw (attempt {Attempts})", item.Id, item.Attempts);
      if (item.Attempts >= maxAttempts) await inbox.Delete(item.Id);
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
    _gate.Dispose();
  }
}
