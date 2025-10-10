using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Vacation.Data;

public class VacationPeriodData
{
  [Key]
  public Guid Id { get; set; }
  public required DateOnly StartDate { get; set; }
  public required DateOnly EndDate { get; set; }
  [MaxLength(256)]
  public required string Timezone { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  
  
  // Foreign Key
  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }
}
