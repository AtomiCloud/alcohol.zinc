using CSharp_Result;
using Domain.Habit;
using Microsoft.Extensions.Logging.Abstractions;
using UnitTest.Penalty;

namespace UnitTest.Protection;

public class HabitUpdateTests
{
  [Theory]
  [InlineData(true, true, true)]
  [InlineData(true, false, true)]
  [InlineData(false, false, true)]
  [InlineData(false, true, false)]
  public async Task AtHabitCap_AllowsEditsButNotReactivation(bool wasEnabled, bool enabled, bool allowed)
  {
    const string userId = "owner";
    var habitId = Guid.NewGuid();
    var repo = new FakeHabitRepository();
    repo.Habits.Add(HabitFakeFactory.Habit(habitId, userId, enabled: wasEnabled));
    var record = HabitFakeFactory.Version(Guid.NewGuid(), habitId).Record with { Task = "Renamed" };
    var entitlements = new FakeEntitlementService
    {
      EnsureHabitsAllowedResult = new InvalidOperationException("Habit cap reached")
    };
    var service = new HabitService(repo, new FakeVacationRepository(), new FakeProtectionRepository(),
      entitlements, new FakePenaltyRepository(), NullLogger<HabitService>.Instance);

    var result = await service.Update(userId, habitId, record, enabled);

    result.IsSuccess().Should().Be(allowed);
    var habit = (await repo.GetHabit(habitId)).Get()!;
    habit.Record.Version.Should().Be(allowed ? (ushort)2 : (ushort)1);
    habit.Record.Enabled.Should().Be(allowed ? enabled : wasEnabled);
    if (allowed) result.Get()!.Record.Task.Should().Be("Renamed");

    // Ownership is checked before any quota decision or update.
    (await service.Update("stranger", habitId, record, enabled)).Get().Should().BeNull();
    (await repo.GetHabit(habitId)).Get().Should().Be(habit);
  }
}
