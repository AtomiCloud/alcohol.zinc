using Domain.Disbursement;
using NodaMoney;

namespace App.Modules.Disbursement.Data;

public static class DisbursementMapper
{
  public static DisbursementPrincipal ToPrincipal(this DisbursementData data)
  {
    return new DisbursementPrincipal
    {
      Id = data.Id,
      Record = new DisbursementRecord
      {
        CharityId = data.CharityId,
        Amount = new Money(data.AmountCents / 100m, Currency.FromCode(data.Currency)),
        PledgeOrganizationId = data.PledgeOrganizationId,
        Status = (DisbursementStatus)data.Status,
        ProviderDonationId = data.ProviderDonationId,
        Attempts = data.Attempts,
        LastError = data.LastError
      },
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };
  }
}
