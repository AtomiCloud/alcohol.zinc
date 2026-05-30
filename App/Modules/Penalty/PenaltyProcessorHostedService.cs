using Domain.Penalty;

namespace App.Modules.Penalty;

public class PenaltyProcessorHostedService(
  IServiceProvider serviceProvider,
  ILogger<PenaltyProcessorHostedService> logger
) : IHostedService, IDisposable
{
  private Timer? _timer;

  // Serialize ticks: if a drain pass runs longer than the 15-min interval the
  // next tick must NOT start a second concurrent DoWork. The gate makes an
  // overlapping tick no-op instead of running a parallel drain. (The atomic
  // claim in GetPending already prevents double-charge across processes; this
  // also avoids redundant in-process work.)
  private readonly SemaphoreSlim _gate = new(1, 1);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    // Drain pending penalties every 15 minutes. Larger initial delay than the
    // failure job so daily failures are marked (enqueued) before draining.
    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15));
    logger.LogInformation("PenaltyProcessorHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    // Non-blocking gate: a tick that arrives while the previous pass is still
    // running simply skips this round rather than piling up.
    if (!await _gate.WaitAsync(0))
    {
      logger.LogInformation("PenaltyProcessor previous pass still running; skipping tick");
      return;
    }
    try
    {
      using var scope = serviceProvider.CreateScope();
      var svc = scope.ServiceProvider.GetRequiredService<IPenaltyService>();
      var res = await svc.ProcessPending(batchSize: 100, maxAttempts: 5);
      if (res.IsSuccess())
      {
        logger.LogInformation("PenaltyProcessor drained {Count}", (int)res);
      }
      else
      {
        logger.LogError(res.FailureOrDefault(), "PenaltyProcessor error");
      }
    }
    catch (Exception e)
    {
      logger.LogError(e, "PenaltyProcessorHostedService failed");
    }
    finally
    {
      _gate.Release();
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    logger.LogInformation("PenaltyProcessorHostedService stopping");
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
    _gate.Dispose();
  }
}
