using CSharp_Result;
using Domain.Check;

namespace Domain.Configuration;

public class ConfigurationService(
  IConfigurationRepository repo,
  ICheckService checkService,
  ITransactionManager tm) : IConfigurationService
{
  public Task<Result<Configuration?>> GetById(Guid id)
  {
    return repo.Get(id);
  }

  public Task<Result<Configuration?>> GetById(Guid id, string userId)
  {
    return repo.Get(id, userId);
  }

  public Task<Result<Configuration?>> GetByUserId(string userId)
  {
    return repo.GetByUserId(userId);
  }

  public Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record,
    Func<ConfigurationPrincipal, Task<Result<Unit>>>? sync)
  {
    return tm.Start(() => repo.Create(userId, record)
      .DoAwait(DoType.MapErrors, config => sync?.Invoke(config) ?? new Unit().ToAsyncResult())
    );
  }

  public async Task<Result<ConfigurationUpdate?>> Update(Guid id, string userId, ConfigurationRecord record)
  {
    var before = await repo.Get(id, userId);
    if (before.IsFailure()) return before.FailureOrDefault();
    var updated = await repo.Update(id, userId, record);
    if (updated.IsFailure()) return updated.FailureOrDefault();
    var principal = updated.Get();
    if (principal == null) return (ConfigurationUpdate?)null;

    // The user's timezone follows the phone: move their pending checks to it, and settle
    // the days that already ended there. Settled checks never change.
    var settled = new List<SettledCheck>();
    if (before.Get()?.Principal.Record.Timezone != record.Timezone)
    {
      var moved = await checkService.MoveToTimezone(userId);
      if (moved.IsFailure()) return moved.FailureOrDefault();
      settled = moved.Get();
    }
    return new ConfigurationUpdate { Principal = principal, SettledChecks = settled };
  }
}
