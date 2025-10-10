using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;

namespace App.Modules.Protection.Data;

public class FreezeAwardData
{
  [Key]
  public Guid Id { get; set; }
  public required DateOnly WeekStart { get; set; }
  public DateTime AwardedAt { get; set; } = DateTime.UtcNow;

  // FK + Navigation (grouped at bottom)
  public required Guid HabitId { get; set; }
  public virtual HabitData? Habit { get; set; }
}
