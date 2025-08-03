using Domain.Configuration;

namespace App.Modules.Configurations.Data
{
    public static class ConfigurationMapper
    {
        public static ConfigurationModel ToDomain(this ConfigurationData data)
        {
            return new ConfigurationModel
            {
                Sub = data.Sub,
                Timezone = data.Timezone,
                EndOfDay = data.EndOfDay,
                DefaultCharityId = data.DefaultCharityId
            };
        }

        public static ConfigurationData ToData(this ConfigurationModel model)
        {
            return new ConfigurationData
            {
                Sub = model.Sub,
                Timezone = model.Timezone,
                EndOfDay = model.EndOfDay,
                DefaultCharityId = model.DefaultCharityId
            };
        }
    }
}
