using CSharp_Result;

namespace Domain.Configuration
{
  public interface IConfigurationRepository
  {
    Task<Result<Configuration?>> Get(Guid id);
    Task<Result<Configuration?>> Get(Guid id, string userId);
    Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record);
    Task<Result<ConfigurationPrincipal?>> Update(ConfigurationPrincipal principal);
  }
}
