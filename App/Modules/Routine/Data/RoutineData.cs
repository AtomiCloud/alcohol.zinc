using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;

namespace App.Modules.Routine.Data;

public class RoutineData
{
  [Key]
  public Guid Id { get; set; }
  [MaxLength(128)]
  public required string UserId { get; set; }
  [MaxLength(256)]
  public required string Name { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  // Soft delete
  public DateTime? DeletedAt { get; set; }

  // Navigation properties
  public virtual ICollection<HabitData> Habits { get; set; } = [];
}
