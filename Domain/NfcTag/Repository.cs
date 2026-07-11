using CSharp_Result;
using Domain.Habit;

namespace Domain.NfcTag;

public interface INfcTagRepository
{
  Task<Result<NfcTagPrincipal?>> Get(string tagId);
  Task<Result<NfcTagPrincipal>> Upsert(string tagId, string userId, Guid habitId);
  Task<Result<Unit?>> Delete(string tagId, string userId);

  /// <summary>
  /// Latest execution of any version of the habit on the given date, or null
  /// when the habit has not been acted on that day.
  /// </summary>
  Task<Result<HabitExecutionPrincipal?>> GetExecutionForDate(Guid habitId, DateOnly date);
}
