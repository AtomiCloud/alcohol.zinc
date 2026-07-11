using App.Modules.Habit.API.V1;
using App.Utility;
using Domain.NfcTag;

namespace App.Modules.NfcTag.API.V1;

public static class NfcTagMapper
{
  public static NfcTagRes ToRes(this NfcTagPrincipal t) =>
    new(
      t.Id,
      t.UserId,
      t.Record.HabitId,
      t.ClaimedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    );

  public static NfcTagResolutionRes ToRes(this NfcTagResolution r) =>
    new(
      r.Principal.ToRes(),
      r.HabitVersion.ToRes(),
      r.Today.ToStandardDateFormat(),
      r.TodayExecution?.ToRes()
    );
}
