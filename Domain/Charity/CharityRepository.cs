using CSharp_Result;

namespace Domain.Charity
{
    public interface ICharityRepository
    {
        Task<Result<CharityModel?>> Get(int id);
        Task<Result<List<CharityModel>>> GetAll();
        Task<Result<CharityModel>> Create(CharityModel model);
        Task<Result<CharityModel?>> Update(CharityModel model);
        Task<Result<Unit?>> Delete(int id);
    }
}
