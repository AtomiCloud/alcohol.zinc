using Domain.Charity;

namespace App.Modules.Charities.Data
{
    public static class CharityMapper
    {
        // Data => Domain (reuse chain)
        public static CharityRecord ToRecord(this CharityData data)
            => new()
            {
                Name = data.Name,
                Slug = data.Slug,
                Mission = data.Mission,
                Countries = data.Countries,
                PrimaryRegistrationNumber = data.PrimaryRegistrationNumber,
                PrimaryRegistrationCountry = data.PrimaryRegistrationCountry,
                WebsiteUrl = data.WebsiteUrl,
                LogoUrl = data.LogoUrl
            };

        public static CharityPrincipal ToPrincipal(this CharityData data)
            => new() { Id = data.Id, Record = data.ToRecord() };

        public static Charity ToDomain(this CharityData data)
            => new() { Principal = data.ToPrincipal() };

        // Domain => Data
        // Variant 1: Create (no Id; DB generates)
        public static CharityData ToData(this CharityRecord record)
            => new()
            {
                Name = record.Name,
                Slug = record.Slug,
                Mission = record.Mission,
                Countries = record.Countries ?? [],
                PrimaryRegistrationNumber = record.PrimaryRegistrationNumber,
                PrimaryRegistrationCountry = record.PrimaryRegistrationCountry,
                WebsiteUrl = record.WebsiteUrl,
                LogoUrl = record.LogoUrl
            };

        // Variant 2: Mutable update on existing Data
        public static CharityData ToData(this CharityData data, CharityRecord record)
        {
            data.Name = record.Name;
            data.Slug = record.Slug;
            data.Mission = record.Mission;
            data.Countries = record.Countries ?? [];
            data.PrimaryRegistrationNumber = record.PrimaryRegistrationNumber;
            data.PrimaryRegistrationCountry = record.PrimaryRegistrationCountry;
            data.WebsiteUrl = record.WebsiteUrl;
            data.LogoUrl = record.LogoUrl;
            return data;
        }
    }
}
