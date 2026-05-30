using System.ComponentModel.DataAnnotations;
using App.Modules.Charities.Data;

namespace App.Modules.Penalty.Data;

public class CharityBalanceData
{
  [Key]
  public Guid Id { get; set; }
  public required Guid CharityId { get; set; } // UNIQUE — one balance row per charity
  public long AccruedCents { get; set; } = 0; // long: accumulates many penalties without overflow
  [MaxLength(8)]
  public required string Currency { get; set; }
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  public virtual CharityData? Charity { get; set; }
}
