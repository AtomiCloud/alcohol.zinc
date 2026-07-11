using Domain.NfcTag;

namespace App.Modules.NfcTag.Data;

public static class NfcTagMapper
{
  public static NfcTagRecord ToRecord(this NfcTagData data)
  {
    return new NfcTagRecord { HabitId = data.HabitId };
  }

  public static NfcTagPrincipal ToPrincipal(this NfcTagData data)
  {
    return new NfcTagPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      ClaimedAt = data.ClaimedAt,
      Record = data.ToRecord()
    };
  }

  // No ToDomain: NfcTag has no aggregate model (Principal is the full shape).
  // No ToData(record): the natural key (tag id) and owner are not part of the
  // Record, so a data row cannot be built from a Record alone.

  public static NfcTagData ToData(this NfcTagData data, NfcTagRecord record)
  {
    data.HabitId = record.HabitId;
    return data;
  }
}
