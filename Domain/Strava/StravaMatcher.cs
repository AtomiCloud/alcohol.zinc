namespace Domain.Strava;

// A Strava habit that an activity can still prove on the day: scheduled, and not skipped,
// frozen, on vacation or done another way. SortTime: habit time, else reminder (null last).
public record MatchHabit(Guid HabitId, TimeOnly? SortTime, int SortOrder, IReadOnlySet<string> SportTypes);

public record MatchActivity(long ActivityId, DateTime StartUtc, string SportType, bool Manual,
  StravaAssignment Assignment);

// Decides which habit each of a day's activities proves. One activity proves at most one habit
// and one habit takes at most one activity. Only non-manual activities of a sport type the
// habit's binding allows count. Pinned choices (the user's) stay; the rest fill as many habits
// as possible, and among equally full assignments earlier activities go to earlier habits.
public static class StravaMatcher
{
  // ActivityId -> HabitId (null: not used), for every given activity.
  public static Dictionary<long, Guid?> Match(IReadOnlyList<MatchHabit> habits,
    IReadOnlyList<MatchActivity> activities)
  {
    var orderedHabits = habits
      .OrderBy(h => h.SortTime.HasValue ? 0 : 1)
      .ThenBy(h => h.SortTime)
      .ThenBy(h => h.SortOrder)
      .ThenBy(h => h.HabitId)
      .ToList();
    var orderedActivities = activities.OrderBy(a => a.StartUtc).ThenBy(a => a.ActivityId).ToList();
    var byId = habits.ToDictionary(h => h.HabitId);

    var result = new Dictionary<long, Guid?>();
    var taken = new HashSet<Guid>();

    // "Not used" pins stay unused. A habit pin holds while its habit is still open and the
    // activity still fits it; otherwise the activity is free again.
    var free = new List<MatchActivity>();
    foreach (var a in orderedActivities)
    {
      if (!a.Assignment.Pinned) free.Add(a);
      else if (a.Assignment.HabitId is not { } id) result[a.ActivityId] = null;
      else if (byId.TryGetValue(id, out var h) && Fits(a, h) && taken.Add(id)) result[a.ActivityId] = id;
      else free.Add(a);
    }

    var open = orderedHabits.Where(h => !taken.Contains(h.HabitId)).ToList();
    var best = MaxMatching(free, open);

    // Fix activities in time order, each to the earliest habit that still lets the rest
    // reach the maximum.
    var used = new HashSet<Guid>();
    var fixedCount = 0;
    for (var i = 0; i < free.Count; i++)
    {
      var a = free[i];
      Guid? chosen = null;
      foreach (var h in open.Where(h => !used.Contains(h.HabitId) && Fits(a, h)))
      {
        var rest = open.Where(o => o.HabitId != h.HabitId && !used.Contains(o.HabitId)).ToList();
        if (fixedCount + 1 + MaxMatching(free.Skip(i + 1).ToList(), rest) < best) continue;
        chosen = h.HabitId;
        break;
      }

      if (chosen is { } c)
      {
        used.Add(c);
        fixedCount++;
      }

      result[a.ActivityId] = chosen;
    }

    return result;
  }

  public static bool Fits(MatchActivity a, MatchHabit h) => !a.Manual && h.SportTypes.Contains(a.SportType);

  // Size of a maximum matching (augmenting paths); a day has a handful of each.
  private static int MaxMatching(List<MatchActivity> activities, List<MatchHabit> habits)
  {
    var owner = new Dictionary<Guid, int>(); // habit -> index of the activity holding it
    var size = 0;
    for (var i = 0; i < activities.Count; i++)
      if (Augment(i, [])) size++;
    return size;

    bool Augment(int i, HashSet<Guid> seen)
    {
      foreach (var h in habits.Where(h => Fits(activities[i], h)))
      {
        if (!seen.Add(h.HabitId)) continue;
        if (owner.TryGetValue(h.HabitId, out var j) && !Augment(j, seen)) continue;
        owner[h.HabitId] = i;
        return true;
      }

      return false;
    }
  }
}
