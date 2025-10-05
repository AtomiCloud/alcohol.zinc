using CSharp_Result;

namespace Domain.Configuration;

public interface IConfigurationService
{
  Task<Result<Configuration?>> GetById(Guid id);
  Task<Result<Configuration?>> GetById(Guid id, string userId);
  Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record, Func<ConfigurationPrincipal, Task<Result<Unit>>>? sync);
  Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record);
}
