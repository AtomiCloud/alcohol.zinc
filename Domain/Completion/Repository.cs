using System.Collections.Generic;
using System.Threading.Tasks;
using CSharp_Result;

namespace Domain.Completion
{
    public interface ICompletionRepository
    {
        Task<Result<CompletionModel>> Get(System.DateOnly date, int taskId);
        Task<Result<List<CompletionModel>>> GetByTaskIdAsync(int taskId);
        Task<Result<CompletionModel>> Create(CompletionModel model);
        Task<Result<Unit?>> Delete(System.DateOnly date, int taskId);
    }
}
