using Domain.Charity;

namespace App.Modules.Charities.Data
{
    public static class CharityMapper
    {
        public static CharityModel ToDomain(this CharityData data)
        {
            return new CharityModel
            {
                Id = data.Id,
                Name = data.Name,
                Email = data.Email
            };
        }

        public static CharityData ToData(this CharityModel model)
        {
            return new CharityData
            {
                Id = model.Id,
                Name = model.Name,
                Email = model.Email
            };
        }
    }
}
