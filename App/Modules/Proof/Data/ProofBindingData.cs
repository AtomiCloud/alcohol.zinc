using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;

namespace App.Modules.Proof.Data;

// A habit's proof plugin and its settings (one per habit). Habits without a row use the
// always-bound proofs only (manual).
public class ProofBindingData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid HabitId { get; set; }
  public virtual HabitData? Habit { get; set; }

  [MaxLength(64)]
  public required string PluginKey { get; set; }

  public required int PluginVersion { get; set; }

  [MaxLength(4096)]
  public required string Settings { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
