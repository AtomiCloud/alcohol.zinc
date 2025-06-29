using CSharp_Result;

namespace Domain.Configuration
{
    public interface IConfigurationService
    {
        Task<Result<ConfigurationModel>> Get(string sub);
        Task<Result<ConfigurationModel?>> Update(ConfigurationModel model);
    }
}
