using CSharp_Result;

namespace Domain.Cause
{
    public interface ICauseRepository
    {
        Task<Result<Cause?>> Get(Guid id);
        Task<Result<IEnumerable<CausePrincipal>>> Search(CauseSearch search);
        Task<Result<CausePrincipal>> Create(CauseRecord model);
        Task<Result<CausePrincipal?>> Update(Guid id, CauseRecord record);
        Task<Result<Unit?>> Delete(Guid id);
        Task<Result<CausePrincipal?>> GetByKey(string key);
    }
}
