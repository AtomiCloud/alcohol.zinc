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

  public async Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
  {
    var before = await repo.Get(id, userId);
    if (before.IsFailure()) return before.FailureOrDefault();
    var updated = await repo.Update(id, userId, record);
    if (updated.IsFailure() || updated.Get() == null) return updated;

    // The user's timezone follows the phone: move their pending checks to it. Settled
    // checks never change, and an open check never gets shorter.
    if (before.Get()?.Principal.Record.Timezone != record.Timezone)
    {
      var retimed = await checkService.RetimePending(userId, null);
      if (retimed.IsFailure()) return retimed.FailureOrDefault();
    }
    return updated;
  }
}
