using Domain.Disbursement;
using NodaMoney;

namespace App.Modules.Disbursement.Data;

public static class DisbursementMapper
{
  // Data => Domain (reuse chain): ToRecord -> ToPrincipal -> ToDomain, each building on the last.
  public static DisbursementRecord ToRecord(this DisbursementData data)
    => new()
    {
      CharityId = data.CharityId,
      Amount = new Money(data.AmountCents / 100m, Currency.FromCode(data.Currency)),
      PledgeOrganizationId = data.PledgeOrganizationId,
      Status = (DisbursementStatus)data.Status,
      ProviderDonationId = data.ProviderDonationId,
      Attempts = data.Attempts,
      LastError = data.LastError
    };

  public static DisbursementPrincipal ToPrincipal(this DisbursementData data)
    => new()
    {
      Id = data.Id,
      Record = data.ToRecord(),
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };

  // Fully qualified: the aggregate type `Disbursement` would otherwise bind to the
  // `App.Modules.Disbursement` namespace from inside this file.
  public static Domain.Disbursement.Disbursement ToDomain(this DisbursementData data)
    => new() { Principal = data.ToPrincipal() };
}
