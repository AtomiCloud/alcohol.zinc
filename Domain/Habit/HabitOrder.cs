using CSharp_Result;
using Domain.Exceptions;

namespace Domain.Habit;

public static class HabitOrder
{
  public const string Field = "habitIds";

  // The user's new habit order: the requested ids first, in the given order, then the
  // ones not requested in their current order. Every requested id must be one of the
  // user's live habits (current), at most once.
  public static Result<List<Guid>> Merge(IReadOnlyList<Guid> current, IReadOnlyList<Guid> requested)
  {
    var currentSet = current.ToHashSet();
    var requestedSet = new HashSet<Guid>();
    foreach (var id in requested)
    {
      if (!requestedSet.Add(id))
        return new InvalidRequestException(Field, $"Habit {id} is listed more than once");
      // Another user's, a deleted and an unknown habit get the same answer.
      if (!currentSet.Contains(id))
        return new InvalidRequestException(Field, $"Habit {id} is not one of your habits");
    }

    return requested.Concat(current.Where(id => !requestedSet.Contains(id))).ToList();
  }
}
