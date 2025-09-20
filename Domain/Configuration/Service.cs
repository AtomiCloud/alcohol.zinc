using CSharp_Result;

namespace Domain.Configuration;

public class ConfigurationService(IConfigurationRepository repo) : IConfigurationService
{
  public Task<Result<Configuration?>> GetById(Guid id)
  {
    return repo.Get(id);
  }

  public Task<Result<Configuration?>> GetById(Guid id, string userId)
  {
    return repo.Get(id, userId);
  }

  public Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record)
  {
    return repo.Create(userId, record);
  }

  public Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
  {
    return repo.Update(id, userId, record);
  }
}
