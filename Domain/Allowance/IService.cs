using CSharp_Result;

namespace Domain.Allowance;

public interface IAllowanceService
{
  Task<Result<UserMonthWindow>> GetUserMonthWindow(string userId, DateTime? utcNow = null);
  Task<Result<(string Timezone, DateOnly Today)>> GetUserToday(string userId, DateTime? utcNow = null);
}
