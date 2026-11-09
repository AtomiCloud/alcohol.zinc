using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.NfcTag.Data;
using App.Modules.Routine.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace IntTest.Routine;

// DB-backed integration tests for routines against a REAL Postgres MainDbContext:
// habit create auto-creates a one-habit routine, cross-user routine ids are rejected,
// deleting the last habit soft deletes its routine, and deleting a routine soft
// deletes its habits (releasing their NFC tags).
//
// Connection comes from the ROUTINE_TEST_DB env var (same format as PENALTY_TEST_DB).
// Unset -> tests Skip, so the suite stays green without a database.
[Collection(DbIntegrationCollection.Name)]
public class RoutineIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "ROUTINE_TEST_DB not set; skipping DB-backed routine integration test.";
  private readonly string? _conn = Environment.GetEnvironmentVariable("ROUTINE_TEST_DB");
  private MainDbContext _db = null!;

  public async Task InitializeAsync()
  {
    if (_conn == null) return;
    _db = NewContext(_conn);
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (_conn == null) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  private static MainDbContext NewContext(string conn)
  {
    var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries)
      .Select(p => p.Split('=', 2))
      .Where(kv => kv.Length == 2)
      .ToDictionary(kv => kv[0].Trim().ToLowerInvariant(), kv => kv[1].Trim());

    var opt = new DatabaseOption
    {
      Host = parts.GetValueOrDefault("host", "localhost"),
      Port = int.TryParse(parts.GetValueOrDefault("port", "5432"), out var p) ? p : 5432,
      Database = parts.GetValueOrDefault("database", "routine_test"),
      User = parts.GetValueOrDefault("username", parts.GetValueOrDefault("user", "postgres")),
      Password = parts.GetValueOrDefault("password", "postgres"),
      AutoMigrate = false,
      Timeout = 30
    };
    var monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    return new MainDbContext(monitor, NullLoggerFactory.Instance);
  }

  private HabitRepository HabitRepo() => new(_db, NullLogger<HabitRepository>.Instance);
  private RoutineRepository RoutineRepo() => new(_db, NullLogger<RoutineRepository>.Instance);

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<HabitVersionRecord> NewVersionRecord(string task)
  {
    var charityId = Guid.NewGuid();
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    await _db.SaveChangesAsync();
    return new HabitVersionRecord
    {
      CharityId = charityId,
      Task = task,
      DaysOfWeek = ["Monday"],
      NotificationTime = new TimeOnly(8, 0),
      Stake = new Money(10m, Currency.FromCode("USD")),
      Ratio = 1.0m,
      Timezone = "Asia/Singapore"
    };
  }

  private async Task<Guid> CreateHabit(string userId, string task, Guid? routineId)
  {
    var created = await HabitRepo().Create(userId, await NewVersionRecord(task), routineId);
    created.IsSuccess().Should().BeTrue();
    return created.Get().HabitId;
  }

  private Task<HabitData> Habit(Guid id) => _db.Habits.AsNoTracking().SingleAsync(x => x.Id == id);
  private Task<RoutineData> Routine(Guid id) => _db.Routines.AsNoTracking().SingleAsync(x => x.Id == id);

  [SkippableFact]
  public async Task CreateHabit_WithoutRoutine_CreatesOneHabitRoutineOwnedByUser()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser();

    var habitId = await CreateHabit(owner, "Morning run", null);

    var habit = await Habit(habitId);
    habit.RoutineId.Should().NotBeNull("new code must always set the routine");
    var routine = await Routine(habit.RoutineId!.Value);
    routine.UserId.Should().Be(owner);
    routine.Name.Should().Be("Morning run");
    routine.DeletedAt.Should().BeNull();
    (await _db.Habits.CountAsync(x => x.RoutineId == routine.Id)).Should().Be(1);
    (await _db.Routines.CountAsync(x => x.UserId == owner)).Should().Be(1);
  }

  [SkippableFact]
  public async Task CreateHabit_WithOtherUsersOrDeletedRoutine_FailsNotFound_AndWritesNothing()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser();
    var stranger = await SeedUser();
    var strangerHabit = await CreateHabit(stranger, "stranger habit", null);
    var strangerRoutineId = (await Habit(strangerHabit)).RoutineId!.Value;

    var hijack = await HabitRepo().Create(owner, await NewVersionRecord("hijack"), strangerRoutineId);

    hijack.IsFailure().Should().BeTrue();
    hijack.FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await _db.Habits.CountAsync(x => x.UserId == owner)).Should().Be(0);
    (await _db.Routines.CountAsync(x => x.UserId == owner)).Should().Be(0, "the failed create must roll back");
    (await _db.Habits.CountAsync(x => x.RoutineId == strangerRoutineId)).Should().Be(1);

    // The owner's own routine, once deleted, is not usable either
    var ownRoutine = (await RoutineRepo().Create(owner, new Domain.Routine.RoutineRecord { Name = "gone" })).Get();
    ((Unit?)await RoutineRepo().Delete(ownRoutine.Id, owner)).Should().NotBeNull();
    var intoDeleted = await HabitRepo().Create(owner, await NewVersionRecord("late"), ownRoutine.Id);
    intoDeleted.FailureOrDefault().Should().BeOfType<NotFoundException>();
  }

  [SkippableFact]
  public async Task DeleteHabit_SoftDeletesRoutineOnlyWhenLastLiveHabitGoes()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser();
    var first = await CreateHabit(owner, "first", null);
    var routineId = (await Habit(first)).RoutineId!.Value;
    var second = await CreateHabit(owner, "second", routineId);
    (await Habit(second)).RoutineId.Should().Be(routineId);

    ((Unit?)await HabitRepo().Delete(first, owner)).Should().NotBeNull();
    (await Routine(routineId)).DeletedAt.Should().BeNull("the routine still has a live habit");

    ((Unit?)await HabitRepo().Delete(second, owner)).Should().NotBeNull();
    (await Routine(routineId)).DeletedAt.Should().NotBeNull("its last habit was deleted");
    ((Domain.Routine.RoutinePrincipal?)await RoutineRepo().Get(routineId)).Should().BeNull();
  }

  [SkippableFact]
  public async Task DeleteRoutine_SoftDeletesItsHabits_ReleasesTags_OwnerOnly()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser();
    var stranger = await SeedUser();
    var first = await CreateHabit(owner, "first", null);
    var routineId = (await Habit(first)).RoutineId!.Value;
    var second = await CreateHabit(owner, "second", routineId);
    var other = await CreateHabit(owner, "other routine", null);
    var tagId = Guid.NewGuid().ToString();
    (await new NfcTagRepository(_db, NullLogger<NfcTagRepository>.Instance).Upsert(tagId, owner, second))
      .IsSuccess().Should().BeTrue();

    ((Unit?)await RoutineRepo().Delete(routineId, stranger)).Should().BeNull("someone else's routine is not found");
    (await Routine(routineId)).DeletedAt.Should().BeNull();

    ((Unit?)await RoutineRepo().Delete(routineId, owner)).Should().NotBeNull();

    (await Routine(routineId)).DeletedAt.Should().NotBeNull("routine deletion is a soft delete");
    (await Habit(first)).DeletedAt.Should().NotBeNull();
    (await Habit(second)).DeletedAt.Should().NotBeNull();
    (await Habit(other)).DeletedAt.Should().BeNull("habits of other routines are untouched");
    (await _db.NfcTags.AsNoTracking().AnyAsync(x => x.Id == tagId))
      .Should().BeFalse("tags of the soft-deleted habits are released");
    ((Unit?)await RoutineRepo().Delete(routineId, owner)).Should().BeNull("a deleted routine is gone");
  }
}
