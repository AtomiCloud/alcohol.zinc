using NodaMoney;

namespace Domain.Penalty;

public enum PenaltyStatus
{
  Pending,
  Charged,
  Failed,
  Skipped,
  Processing, // claimed by a drain worker (atomic Pending->Processing lease); excluded from GetPending so concurrent drains cannot double-charge.
  Held // its charity was decommissioned before the money was taken: kept as a record, never charged and never paid out, until someone decides what to do with it
}

// Thrown when a penalty can no longer take a payment because its charity was decommissioned while
// the charge was in flight. Raised before the payment is confirmed, so no money moves.
public class PenaltyHeldException(Guid penaltyId)
  : InvalidOperationException($"Penalty {penaltyId} is on hold: its charity was decommissioned.");

public record PenaltyRecord
{
  // Idempotency key (one penalty per execution). Null once the execution was deleted with its user.
  public required Guid? HabitExecutionId { get; init; }
  public required string? UserId { get; init; } // null once the user was deleted (detached ledger row)
  public required Guid CharityId { get; init; }
  public required Money Amount { get; init; } // NodaMoney.Money; reconstructed at read boundary from cents+currency
  public required PenaltyStatus Status { get; init; }
  public string? PaymentIntentId { get; init; }
  public required int Attempts { get; init; }
  public string? LastError { get; init; }
  // Payout linkage: null while pending payout, set once a disbursement has claimed it.
  public Guid? DisbursementId { get; init; }
}

public record PenaltyPrincipal
{
  public required Guid Id { get; init; }
  public required PenaltyRecord Record { get; init; }
  public required DateTime CreatedAt { get; init; }
  public required DateTime UpdatedAt { get; init; }
}

public record Penalty
{
  public required PenaltyPrincipal Principal { get; init; }
}

public record PenaltySearch
{
  public string? UserId { get; init; }
  public PenaltyStatus? Status { get; init; }
  public required int Limit { get; init; }
  public required int Skip { get; init; }
}

public record CharityBalanceRecord
{
  public required Money Accrued { get; init; }
}

public record CharityBalancePrincipal
{
  public required Guid CharityId { get; init; }
  public required CharityBalanceRecord Record { get; init; }
  public required DateTime UpdatedAt { get; init; }
}

// A missed habit day the user still owes: its execution failed and its penalty is not Charged (or Held,
// which the user did not cause). Status is the penalty's, null while no penalty row exists yet.
public record OwedItem
{
  public required Guid ExecutionId { get; init; }
  public required Guid HabitId { get; init; }
  public required string HabitName { get; init; }
  public required DateOnly LocalDate { get; init; }
  public required decimal Amount { get; init; }
  public required string Currency { get; init; }
  public required Guid? CharityId { get; init; }
  public required string? CharityName { get; init; }
  public required PenaltyStatus? Status { get; init; }

  // Skipped: there was no verified card. Failed: every attempt failed. Either waits for the user to
  // add or fix their card (which re-queues it); anything else is still being charged.
  public OwedReason? CouldNotCharge => Status switch
  {
    PenaltyStatus.Skipped => OwedReason.NoCard,
    PenaltyStatus.Failed => OwedReason.ChargeFailed,
    _ => null
  };
}

public enum OwedReason
{
  NoCard,
  ChargeFailed
}
