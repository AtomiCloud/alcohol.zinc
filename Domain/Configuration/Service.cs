using CSharp_Result;

namespace Domain.Configuration;

public class ConfigurationService(IConfigurationRepository repo) : IConfigurationService
{
  public Task<Result<Configuration?>> GetByUserId(string userId)
  {
    return repo.Get(userId);
  }

  public Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record)
  {
    return repo.Create(userId, record);
  }

  public Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record)
  {
    var principal = new ConfigurationPrincipal
    {
      Id = id,
      UserId = userId,
      Record = record
    };
    return repo.Update(principal);
  }

  public Task<Result<Unit?>> Delete(string userId)
  {
    return repo.Delete(userId);
  }
}
