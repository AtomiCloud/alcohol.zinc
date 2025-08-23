using CSharp_Result;

namespace Domain.Configuration
{
  public interface IConfigurationRepository
  {
    Task<Result<Configuration?>> Get(string userId);
    Task<Result<ConfigurationPrincipal>> Create(string userId, ConfigurationRecord record);
    Task<Result<ConfigurationPrincipal?>> Update(ConfigurationPrincipal principal);
    Task<Result<Unit?>> Delete(string userId);
  }
}
