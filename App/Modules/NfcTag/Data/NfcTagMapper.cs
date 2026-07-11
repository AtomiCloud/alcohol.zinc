using Domain.NfcTag;

namespace App.Modules.NfcTag.Data;

public static class NfcTagMapper
{
  public static NfcTagPrincipal ToPrincipal(this NfcTagData data)
  {
    return new NfcTagPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      ClaimedAt = data.ClaimedAt,
      Record = new NfcTagRecord { HabitId = data.HabitId }
    };
  }
}
