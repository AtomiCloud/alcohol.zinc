using CSharp_Result;

namespace Domain.Configuration;

public class ConfigurationService(
  IConfigurationRepository repo,
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

  public Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record,
    Func<ConfigurationPrincipal, Task<Result<Unit>>>? sync)
  {
    return tm.Start(() => repo.Create(userId, record)
      .DoAwait(DoType.MapErrors, config => sync?.Invoke(config) ?? new Unit().ToAsyncResult())
    );
  }

  public Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
  {
    return repo.Update(id, userId, record);
  }
}
