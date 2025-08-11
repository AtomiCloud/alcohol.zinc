using Domain.Charity;

namespace App.Modules.Charities.Data
{
    public static class CharityMapper
    {
        public static Charity ToDomain(this CharityData data)
        {
            return new Charity
            {
                Principal = new CharityPrincipal
                {
                    Id = data.Id,
                    Record = new CharityRecord
                    {
                        Name = data.Name,
                        Email = data.Email,
                        Address = data.Address
                    }
                }
            };
        }

        public static CharityData ToData(this CharityPrincipal principal)
        {
            return new CharityData
            {
                Id = principal.Id,
                Name = principal.Record.Name,
                Email = principal.Record.Email,
                Address = principal.Record.Address
            };
        }

        public static CharityPrincipal ToPrincipal(this CharityData data)
        {
            return new CharityPrincipal
            {
                Id = data.Id,
                Record = new CharityRecord
                {
                    Name = data.Name,
                    Email = data.Email,
                    Address = data.Address
                }
            };
        }

        public static CharityData ToData(this CharityRecord record)
        {
            return new CharityData
            {
                Id = Guid.NewGuid(),
                Name = record.Name,
                Email = record.Email,
                Address = record.Address
            };
        }
    }
}
