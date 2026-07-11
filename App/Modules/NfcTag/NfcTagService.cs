using App.Error.V1;
using App.Utility;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using Domain.NfcTag;

namespace App.Modules.NfcTag;

public class NfcTagService(
  INfcTagRepository repo,
  IHabitService habitService
) : INfcTagService
{
  public Task<Result<NfcTagPrincipal>> Link(string userId, string tagId, Guid habitId)
  {
    return habitService.GetCurrentHabitVersion(userId, habitId)
      .ThenAwait(async hv =>
      {
        if (hv == null)
          return (Result<NfcTagPrincipal>)new NotFoundException("Habit Not Found", typeof(HabitPrincipal), habitId.ToString());

        return await repo.Get(tagId)
          .ThenAwait<NfcTagPrincipal?, NfcTagPrincipal>(async existing =>
          {
            if (existing != null && existing.UserId != userId)
              return new EntityConflict("NFC tag is already linked by another user", typeof(NfcTagPrincipal))
                .ToException();

            return await repo.Upsert(tagId, userId, habitId);
          });
      });
  }

  public Task<Result<NfcTagResolution?>> Resolve(string userId, string tagId)
  {
    return repo.Get(tagId)
      .ThenAwait(async tag =>
      {
        // Unclaimed and foreign-owned look identical to the caller (404):
        // resolving must never leak who owns a tag.
        if (tag == null || tag.UserId != userId)
          return (Result<NfcTagResolution?>)(NfcTagResolution?)null;

        return await habitService.GetCurrentHabitVersion(userId, tag.Record.HabitId)
          .ThenAwait<HabitVersionPrincipal?, NfcTagResolution?>(async hv =>
          {
            if (hv == null) return (NfcTagResolution?)null;

            var tz = TimeZoneInfo.FindSystemTimeZoneById(hv.Record.Timezone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

            return await repo.GetExecutionForDate(tag.Record.HabitId, today)
              .Then(exec => (NfcTagResolution?)new NfcTagResolution
              {
                Principal = tag,
                HabitVersion = hv,
                Today = today,
                TodayExecution = exec
              }, Errors.MapNone);
          });
      });
  }

  public Task<Result<Unit?>> Unlink(string userId, string tagId)
  {
    return repo.Delete(tagId, userId);
  }
}
