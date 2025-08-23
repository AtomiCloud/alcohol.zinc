using CSharp_Result;

namespace Domain.Configuration;

public interface IConfigurationService
{
  Task<Result<Configuration?>> GetByUserId(string userId);
  Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record);
  Task<Result<ConfigurationPrincipal?>> Update(Guid id, string userId, ConfigurationRecord record);
  Task<Result<Unit?>> Delete(string userId);
}
