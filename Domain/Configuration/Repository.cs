using CSharp_Result;

namespace Domain.Configuration
{
    public interface IConfigurationRepository
    {
        Task<Result<ConfigurationModel?>> Get(string sub);
        Task<Result<ConfigurationModel>> Create(ConfigurationModel model);
        Task<Result<ConfigurationModel?>> Update(ConfigurationModel model);
        Task<Result<Unit?>> Delete(string sub);
    }
}
