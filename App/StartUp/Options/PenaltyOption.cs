namespace App.StartUp.Options;

// Penalty (auto-deduction) settings. The processor charges failed staked habits
// against the user's stored Airwallex consent — real money movement — so it
// only runs where deliberately enabled.
public class PenaltyOption
{
  public const string Key = "Penalty";

  // Master switch for the background charge worker. Off by default so promoting
  // a landscape can never silently start deducting money (e.g. raichu while the
  // Airwallex account migration is in flight). Failure marking still runs and
  // penalties still accrue while disabled — flipping this on later drains that
  // backlog, so clear stale penalties first if that is not intended.
  public bool Enabled { get; set; } = false;
}
