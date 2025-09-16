using App.Modules.Charities.Data;
using Domain.Configuration;

namespace App.Modules.Configurations.Data
{
    public static class ConfigurationMapper
    {
        public static Configuration ToDomain(this ConfigurationData data)
        {
            return new Configuration
            {
                Principal = new ConfigurationPrincipal
                {
                    Id = data.Id,
                    UserId = data.UserId,
                    Record = new ConfigurationRecord
                    {
                        Timezone = data.Timezone,
                        DefaultCharityId = data.DefaultCharityId
                    }
                },
                Charity = data.Charity?.ToPrincipal()
            };
        }

        public static ConfigurationData ToData(this ConfigurationPrincipal principal)
        {
            return new ConfigurationData
            {
                Id = principal.Id,
                UserId = principal.UserId,
                Timezone = principal.Record.Timezone,
                EndOfDay = new TimeOnly(23, 59),
                DefaultCharityId = principal.Record.DefaultCharityId
            };
        }

        public static ConfigurationPrincipal ToPrincipal(this ConfigurationData data)
        {
            return new ConfigurationPrincipal
            {
                Id = data.Id,
                UserId = data.UserId,
                Record = new ConfigurationRecord
                {
                    Timezone = data.Timezone,
                    DefaultCharityId = data.DefaultCharityId
                }
            };
        }

        public static ConfigurationData ToData(this ConfigurationRecord record, Guid id, string userId)
        {
            return new ConfigurationData
            {
                Id = id,
                UserId = userId,
                Timezone = record.Timezone,
                EndOfDay = new TimeOnly(23, 59),
                DefaultCharityId = record.DefaultCharityId
            };
        }
    }
}
