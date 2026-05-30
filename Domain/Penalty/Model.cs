using NodaMoney;

namespace Domain.Penalty;

public enum PenaltyStatus
{
  Pending,
  Charged,
  Failed,
  Skipped,
  Processing // claimed by a drain worker (atomic Pending->Processing lease); excluded from GetPending so concurrent drains cannot double-charge.
}

public record PenaltyRecord
{
  public required Guid HabitExecutionId { get; init; } // idempotency key (one penalty per execution)
  public required string UserId { get; init; }
  public required Guid CharityId { get; init; }
  public required Money Amount { get; init; } // NodaMoney.Money; reconstructed at read boundary from cents+currency
  public required PenaltyStatus Status { get; init; }
  public string? PaymentIntentId { get; init; }
  public required int Attempts { get; init; }
  public string? LastError { get; init; }
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
