using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

// Charity payout (disbursement) settings. The donor fields are the legal donor of record
// (LazyTax) recorded on every Pledge donation; they match the public legal pages.
public class DisbursementOption
{
  public const string Key = "Disbursement";

  // Master switch for the background payout worker. Off by default so the job only runs where
  // it has been deliberately enabled (e.g. pichu, pointed at Pledge staging).
  public bool Enabled { get; set; } = false;

  // Skip donating a (charity, currency) group whose accrued total is below this, to avoid
  // per-donation overhead on trivial amounts. 0 = no threshold.
  [Range(0, long.MaxValue)] public long MinPayoutCents { get; set; } = 0;

  // Cap on how many (charity, currency) groups are paid out per run.
  [Range(1, int.MaxValue)] public int MaxGroupsPerRun { get; set; } = 100;

  [Required] public string DonorFirstName { get; set; } = "LazyTax";
  [Required] public string DonorLastName { get; set; } = "Donations";
  [Required, EmailAddress] public string DonorEmail { get; set; } = "donations@lazytax.club";
}
