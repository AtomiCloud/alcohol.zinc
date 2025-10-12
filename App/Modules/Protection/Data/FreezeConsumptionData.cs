using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Protection.Data;

public class FreezeConsumptionData
{
  [Key]
  public Guid Id { get; set; }
  public required DateOnly Date { get; set; }
  public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;

  // FK + Navigation (grouped at bottom)
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }
}
