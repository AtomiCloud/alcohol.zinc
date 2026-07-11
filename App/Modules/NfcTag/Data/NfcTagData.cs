using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;
using App.Modules.Users.Data;

namespace App.Modules.NfcTag.Data;

public class NfcTagData
{
  // Tag id embedded in the physical tag's URL (lazytax.club/t/{id}) — natural key.
  [Key, MaxLength(64)]
  public required string Id { get; set; }

  public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;

  // Foreign Keys
  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }

  public required Guid HabitId { get; set; }
  public virtual HabitData? Habit { get; set; }
}
