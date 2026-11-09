using CSharp_Result;
using Domain.Habit;
using Domain.Penalty;
using NodaMoney;

namespace Domain.Check;

// The existing charity penalty, as the first stake binding: a missed check enqueues one
// Pending penalty for its execution (idempotent on the execution id).
public class PenaltyStakeBinding(ICheckRepository repo, IPenaltyRepository penaltyRepo) : IStakeBinding
{
  public string BindingId => "penalty";

  public bool AppliesTo(CheckEventPrincipal e) => e.Outcome == CheckStatus.Missed && e.ExecutionId != null;

  public async Task<Result<Unit>> Deliver(CheckEventPrincipal e)
  {
    var row = (FailedExecutionRow?)await repo.GetStakeForExecution(e.ExecutionId!.Value);
    if (row == null) return new Unit();

    // Exact cents math (mirrors StreakRepository): cents * bps / 10000
    var amountCents = (long)row.StakeCents * row.RatioBasisPoints / 10_000L;
    if (amountCents <= 0) return new Unit();

    await penaltyRepo.EnqueuePending(new PenaltyRecord
    {
      HabitExecutionId = row.ExecutionId,
      UserId = row.UserId,
      CharityId = row.CharityId,
      Amount = new Money(amountCents / 100m, Currency.FromCode(row.StakeCurrency)),
      Status = PenaltyStatus.Pending,
      PaymentIntentId = null,
      Attempts = 0,
      LastError = null
    });
    return new Unit();
  }
}
