using Domain.Penalty;
using NodaMoney;

namespace App.Modules.Penalty.Data;

public static class PenaltyMapper
{
  public static PenaltyData ToData(this PenaltyRecord record)
  {
    return new PenaltyData
    {
      // Id is intentionally unset: EF/Npgsql generates it on add
      // (ValueGeneratedOnAdd), keeping this mapper identity-free.
      HabitExecutionId = record.HabitExecutionId,
      AmountCents = (int)(record.Amount.Amount * 100),
      Currency = record.Amount.Currency.Code,
      Status = (int)record.Status,
      PaymentIntentId = record.PaymentIntentId,
      Attempts = record.Attempts,
      LastError = record.LastError,
      UserId = record.UserId,
      CharityId = record.CharityId
    };
  }

  public static PenaltyRecord ToRecord(this PenaltyData data)
  {
    return new PenaltyRecord
    {
      HabitExecutionId = data.HabitExecutionId,
      UserId = data.UserId,
      CharityId = data.CharityId,
      Amount = new Money(data.AmountCents / 100m, Currency.FromCode(data.Currency)),
      Status = (PenaltyStatus)data.Status,
      PaymentIntentId = data.PaymentIntentId,
      Attempts = data.Attempts,
      LastError = data.LastError
    };
  }

  public static PenaltyPrincipal ToPrincipal(this PenaltyData data)
  {
    return new PenaltyPrincipal
    {
      Id = data.Id,
      Record = data.ToRecord(),
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };
  }

  public static CharityBalancePrincipal ToPrincipal(this CharityBalanceData data)
  {
    return new CharityBalancePrincipal
    {
      CharityId = data.CharityId,
      Record = new CharityBalanceRecord
      {
        Accrued = new Money(data.AccruedCents / 100m, Currency.FromCode(data.Currency))
      },
      UpdatedAt = data.UpdatedAt
    };
  }
}
