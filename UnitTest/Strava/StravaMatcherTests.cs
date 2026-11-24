using Domain.Strava;

namespace UnitTest.Strava;

// Which habit each of a day's Strava activities proves.
public class StravaMatcherTests
{
  private static readonly Guid Morning = Guid.Parse("00000000-0000-0000-0000-000000000009");
  private static readonly Guid Evening = Guid.Parse("00000000-0000-0000-0000-000000000001");
  private static readonly HashSet<string> Run = ["Run"];

  private static MatchHabit Habit(Guid id, int? hour, IReadOnlySet<string>? sports = null, int sortOrder = 0) =>
    new(id, hour is { } h ? new TimeOnly(h, 0) : null, sortOrder, sports ?? Run);

  private static MatchActivity Act(long id, int hour, int minute = 0, string sport = "Run", bool manual = false,
    StravaAssignment? assignment = null) =>
    new(id, new DateTime(2026, 1, 5, hour, minute, 0, DateTimeKind.Utc), sport, manual,
      assignment ?? new StravaAssignment(null, false));

  // Evening's id sorts first, so a matcher ordering by id instead of time gets these wrong.
  private static readonly MatchHabit[] TwoHabits = [Habit(Evening, 18), Habit(Morning, 9)];

  [Fact]
  public void EachRun_GoesToTheHabitOfItsTime()
  {
    var m = StravaMatcher.Match(TwoHabits, [Act(2, 18, 30), Act(1, 9, 10)]);

    m.Should().Equal(new Dictionary<long, Guid?> { [1] = Morning, [2] = Evening });
  }

  [Fact]
  public void OneEveningRun_FillsTheFirstHabit()
  {
    StravaMatcher.Match(TwoHabits, [Act(2, 18, 30)]).Should().Equal(new Dictionary<long, Guid?> { [2] = Morning });
  }

  // Greedy in time order gives the morning Run to the morning habit and then the Ride fits
  // nothing: 1 habit. The maximum is 2.
  [Fact]
  public void SportLimits_FillAsManyHabitsAsPossible()
  {
    HashSet<string> runOrRide = ["Run", "Ride"];
    var habits = new[] { Habit(Morning, 9, runOrRide), Habit(Evening, 18, Run) };

    var m = StravaMatcher.Match(habits, [Act(1, 8), Act(2, 10, sport: "Ride")]);

    m.Should().Equal(new Dictionary<long, Guid?> { [1] = Evening, [2] = Morning });
  }

  [Fact]
  public void ExtraAndManualActivities_StayUnused()
  {
    var m = StravaMatcher.Match(TwoHabits, [Act(1, 7, manual: true), Act(2, 8), Act(3, 9), Act(4, 10)]);

    m.Should().Equal(new Dictionary<long, Guid?> { [1] = null, [2] = Morning, [3] = Evening, [4] = null });
  }

  [Fact]
  public void HabitsWithoutATime_ComeLast_ThenByOrder()
  {
    var noTimeFirstInOrder = Guid.NewGuid();
    var noTimeSecond = Guid.NewGuid();
    var habits = new[]
    {
      Habit(noTimeSecond, null, sortOrder: 2), Habit(noTimeFirstInOrder, null, sortOrder: 1), Habit(Evening, 18)
    };

    var m = StravaMatcher.Match(habits, [Act(1, 6), Act(2, 7), Act(3, 8)]);

    m.Should().Equal(new Dictionary<long, Guid?> { [1] = Evening, [2] = noTimeFirstInOrder, [3] = noTimeSecond });
  }

  [Fact]
  public void Pins_AreKept_AndTheRestFillsAround()
  {
    var toEvening = StravaMatcher.Match(TwoHabits,
      [Act(1, 9, 10, assignment: new StravaAssignment(Evening, true)), Act(2, 18, 30)]);
    var notUsed = StravaMatcher.Match(TwoHabits,
      [Act(1, 9, 10, assignment: new StravaAssignment(null, true)), Act(2, 18, 30)]);

    toEvening.Should().Equal(new Dictionary<long, Guid?> { [1] = Evening, [2] = Morning });
    notUsed.Should().Equal(new Dictionary<long, Guid?> { [1] = null, [2] = Morning });
  }

  // A pin to a habit that is no longer open (done another way) frees the activity.
  [Fact]
  public void PinToAClosedHabit_IsMatchedAgain()
  {
    var m = StravaMatcher.Match([Habit(Morning, 9)], [Act(1, 18, assignment: new StravaAssignment(Evening, true))]);

    m.Should().Equal(new Dictionary<long, Guid?> { [1] = Morning });
  }
}
