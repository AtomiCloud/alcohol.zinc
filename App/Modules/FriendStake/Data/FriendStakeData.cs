using System.ComponentModel.DataAnnotations;
using App.Modules.Habit.Data;
using App.Modules.Users.Data;

namespace App.Modules.FriendStake.Data;

// A friend told when the habit's day ends Missed.
public class HabitFriendWatcherData
{
  [Key]
  public Guid Id { get; set; }

  public required Guid HabitId { get; set; }
  public virtual HabitData? Habit { get; set; }

  [MaxLength(128)]
  public required string FriendUserId { get; set; }
  public virtual UserData? FriendUser { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
