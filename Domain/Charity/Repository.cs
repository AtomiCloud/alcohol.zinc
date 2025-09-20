using CSharp_Result;

namespace Domain.Charity
{
    public interface ICharityRepository
    {
        Task<Result<Charity?>> Get(Guid id);
        Task<Result<IEnumerable<CharityPrincipal>>> GetAll();
        Task<Result<CharityPrincipal>> Create(CharityRecord model);
        Task<Result<CharityPrincipal?>> Update(CharityPrincipal model);
        Task<Result<Unit?>> Delete(Guid id);
    }
}
