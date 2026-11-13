using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

public class HabitOption
{
  public const string Key = "Habit";

  // The largest stake a habit may carry, in cents. Stakes are stored as int cents.
  [Range(0, int.MaxValue)] public int MaxStakeCents { get; set; } = 1_000_000;
}
