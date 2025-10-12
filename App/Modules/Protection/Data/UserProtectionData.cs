using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Protection.Data;

public class UserProtectionData
{
  [Key] public Guid Id { get; set; }
  public required int FreezeCurrent { get; set; }
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // FK + Navigation (grouped at bottom)
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }
}
