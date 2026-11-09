using Domain.Check;

namespace App.Modules.Check;

// Every few minutes: open upcoming checks, settle checks past deadline + grace
// (vacation, freeze, then missed), and dispatch settled-check events to stakes.
// Every step is idempotent, so overlapping ticks across pods are safe.
public class CheckSchedulerHostedService(
  IServiceProvider serviceProvider,
  ILogger<CheckSchedulerHostedService> logger
) : IHostedService, IDisposable
{
  private Timer? _timer;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
    logger.LogInformation("CheckSchedulerHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    if (!await _gate.WaitAsync(0))
    {
      logger.LogInformation("CheckScheduler previous tick still running; skipping");
      return;
    }
    try
    {
      using var scope = serviceProvider.CreateScope();
      var checks = scope.ServiceProvider.GetRequiredService<ICheckService>();
      var res = await checks.Tick(DateTime.UtcNow);
      if (res.IsFailure())
        logger.LogError(res.FailureOrDefault(), "CheckScheduler tick failed");
    }
    catch (Exception e)
    {
      logger.LogError(e, "CheckSchedulerHostedService failed");
    }
    finally
    {
      _gate.Release();
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    logger.LogInformation("CheckSchedulerHostedService stopping");
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
    _gate.Dispose();
  }
}
