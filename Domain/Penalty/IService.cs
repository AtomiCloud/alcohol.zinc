using CSharp_Result;

namespace Domain.Penalty;

public interface IPenaltyService
{
  // Drain a batch of Pending penalties; returns count of rows transitioned out of Pending this run.
  Task<Result<int>> ProcessPending(int batchSize, int maxAttempts);
}
