using Domain.Proof;

namespace App.Modules.Proof.Data;

public static class ProofBindingMapper
{
  public static ProofBindingData ToData(this ProofBindingRecord record, Guid habitId)
  {
    return new ProofBindingData
    {
      HabitId = habitId,
      PluginKey = record.PluginKey,
      PluginVersion = record.PluginVersion,
      Settings = record.Settings
    };
  }

  public static ProofBindingData ToData(this ProofBindingData data, ProofBindingRecord record)
  {
    data.PluginKey = record.PluginKey;
    data.PluginVersion = record.PluginVersion;
    data.Settings = record.Settings;
    data.UpdatedAt = DateTime.UtcNow;
    return data;
  }

  public static ProofBindingRecord ToRecord(this ProofBindingData data)
  {
    return new ProofBindingRecord
    {
      PluginKey = data.PluginKey,
      PluginVersion = data.PluginVersion,
      Settings = data.Settings
    };
  }

  public static ProofBindingPrincipal ToPrincipal(this ProofBindingData data)
  {
    return new ProofBindingPrincipal
    {
      Id = data.Id,
      HabitId = data.HabitId,
      Record = data.ToRecord()
    };
  }

  // Needs the Habit navigation loaded for the owner.
  public static ProofBinding ToDomain(this ProofBindingData data)
  {
    return new ProofBinding
    {
      Principal = data.ToPrincipal(),
      UserId = data.Habit!.UserId
    };
  }
}
