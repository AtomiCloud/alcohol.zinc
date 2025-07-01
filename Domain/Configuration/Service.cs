using CSharp_Result;

namespace Domain.Configuration
{
    public class ConfigurationService(IConfigurationRepository repository) : IConfigurationService
    {
        public Task<Result<ConfigurationModel?>> Get(string sub)
        {
            return repository.Get(sub);
        }

        public Task<Result<ConfigurationModel?>> Update(ConfigurationModel model)
        {
            return repository.Update(model);
        }
    }
}
