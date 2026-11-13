using App.Modules.Charities.Data;
using App.Modules.Habit.API.V1;
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
// deletes its habits (releasing their NFC tags). Also the user's habit order (SortOrder):
// new habits go last, reorder, stable search paging, and the reorder endpoint.
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
    var created = await HabitRepo().Create(userId, await NewVersionRecord(task), routineId, IntTest.Check.Checks.AnyHabits);
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

    var hijack = await HabitRepo().Create(owner, await NewVersionRecord("hijack"), strangerRoutineId, IntTest.Check.Checks.AnyHabits);

    hijack.IsFailure().Should().BeTrue();
    hijack.FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await _db.Habits.CountAsync(x => x.UserId == owner)).Should().Be(0);
    (await _db.Routines.CountAsync(x => x.UserId == owner)).Should().Be(0, "the failed create must roll back");
    (await _db.Habits.CountAsync(x => x.RoutineId == strangerRoutineId)).Should().Be(1);

    // The owner's own routine, once deleted, is not usable either
    var ownRoutine = (await RoutineRepo().Create(owner, new Domain.Routine.RoutineRecord { Name = "gone" })).Get();
    ((Unit?)await RoutineRepo().Delete(ownRoutine.Id, owner)).Should().NotBeNull();
    var intoDeleted = await HabitRepo().Create(owner, await NewVersionRecord("late"), ownRoutine.Id, IntTest.Check.Checks.AnyHabits);
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

  // ---------------------------------------------------------------------------
  // Habit order
  // ---------------------------------------------------------------------------

  private HabitService HabitSvc() => new(HabitRepo(), null!, null!, null!, NullLogger<HabitService>.Instance);

  private async Task<List<Guid>> SearchIds(string userId, int limit = 100, int skip = 0)
    => (await HabitRepo().SearchHabits(new HabitSearch { UserId = userId, Limit = limit, Skip = skip }))
      .Get().Select(x => x.HabitId).ToList();

  private Task<int> SortOrderOf(Guid id) => _db.Habits.AsNoTracking().Where(x => x.Id == id).Select(x => x.SortOrder).SingleAsync();

  // Names sort Z, A, M: creation order, not the name, decides where a new habit goes.
  private async Task<(string Owner, Guid A, Guid B, Guid C)> OwnerWithThreeHabits()
  {
    var owner = await SeedUser();
    var a = await CreateHabit(owner, "Zeta", null);
    var b = await CreateHabit(owner, "Alpha", null);
    var c = await CreateHabit(owner, "Mid", null);
    return (owner, a, b, c);
  }

  [SkippableFact]
  public async Task Create_NewHabits_GetIncreasingSortOrder()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();

    (await SortOrderOf(a)).Should().Be(0);
    (await SortOrderOf(b)).Should().Be(1);
    (await SortOrderOf(c)).Should().Be(2);
    (await SearchIds(owner)).Should().Equal(a, b, c);
  }

  [SkippableFact]
  public async Task SetOrder_ThenSearch_ReturnsNewOrderAcrossPages()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();

    (await HabitRepo().SetOrder(owner, [c, a, b])).IsSuccess().Should().BeTrue();

    (await SearchIds(owner)).Should().Equal(c, a, b);
    (await SearchIds(owner, limit: 2)).Should().Equal(c, a);
    (await SearchIds(owner, limit: 2, skip: 2)).Should().Equal(b);
    (await HabitRepo().GetOrderedIds(owner)).Get().Should().Equal(c, a, b);
  }

  [SkippableFact]
  public async Task Create_AfterReorder_GoesToBottom()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();
    (await HabitSvc().Reorder(owner, [c, a])).Get().Should().Equal(c, a, b);

    var d = await CreateHabit(owner, "Aardvark", null);

    (await SearchIds(owner)).Should().Equal(c, a, b, d);
  }

  [SkippableFact]
  public async Task Update_KeepsSortOrder()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();
    await HabitRepo().SetOrder(owner, [c, a, b]);
    var record = await NewVersionRecord("Renamed");

    // A new version and disabling, then enabling again.
    ((HabitVersionPrincipal?)await HabitRepo().Update(a, owner, record, false)).Should().NotBeNull();
    ((HabitVersionPrincipal?)await HabitRepo().Update(a, owner, record with { Task = "Again" }, true)).Should().NotBeNull();

    (await SearchIds(owner)).Should().Equal(c, a, b);
    var habit = ((HabitPrincipal?)await HabitRepo().GetHabit(a))!;
    habit.Record.Version.Should().Be(3);
    habit.ToRes().SortOrder.Should().Be(1);
  }

  [SkippableFact]
  public async Task Delete_ThenCreate_NewHabitStillLast()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();
    ((Unit?)await HabitRepo().Delete(b, owner)).Should().NotBeNull();

    var d = await CreateHabit(owner, "Aardvark", null);

    // The gap stays: D comes after C (2), not at the habit count (2).
    (await SortOrderOf(d)).Should().Be(3);
    (await SearchIds(owner)).Should().Equal(a, c, d);
  }

  [SkippableFact]
  public async Task SetOrder_WithOtherUsersHabitId_LeavesThatRowUnchanged()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, _) = await OwnerWithThreeHabits();
    var stranger = await SeedUser();
    var s1 = await CreateHabit(stranger, "stranger first", null);
    var s2 = await CreateHabit(stranger, "stranger second", null);

    await HabitRepo().SetOrder(owner, [s2, b, a, s1]);

    (await SortOrderOf(s1)).Should().Be(0);
    (await SortOrderOf(s2)).Should().Be(1);
    (await SearchIds(stranger)).Should().Equal(s1, s2);
    (await SortOrderOf(b)).Should().Be(1);
    (await SortOrderOf(a)).Should().Be(2);
  }

  // A duplicate, another user's, a deleted and an unknown id: refused, nothing written.
  [SkippableFact]
  public async Task Reorder_WithInvalidIds_FailsAndChangesNothing()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();
    var deleted = await CreateHabit(owner, "gone", null);
    ((Unit?)await HabitRepo().Delete(deleted, owner)).Should().NotBeNull();
    var stranger = await SeedUser();
    var foreign = await CreateHabit(stranger, "stranger", null);

    foreach (var ids in new List<Guid>[] { [c, c], [c, foreign], [c, deleted], [c, Guid.NewGuid()] })
    {
      var result = await HabitSvc().Reorder(owner, ids);
      result.FailureOrDefault().Should().BeOfType<InvalidRequestException>()
        .Which.Errors.Should().ContainKey("habitIds");
    }

    (await SearchIds(owner)).Should().Equal(a, b, c);
    (await SortOrderOf(c)).Should().Be(2);
    (await SortOrderOf(foreign)).Should().Be(0);
  }

  [SkippableFact]
  public async Task ReorderEndpoint_SavesOrder_RefusesOtherUser_AndRejectsBadIds()
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, a, b, c) = await OwnerWithThreeHabits();
    var stranger = await SeedUser();
    var controller = new HabitController(HabitSvc(), null!, null!, null!, null!,
      null!, null!, new(), new(), new(), new())
    {
      ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
      {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
          User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, owner)], "test"))
        }
      }
    };
    static int? Status(Microsoft.AspNetCore.Mvc.ActionResult<ReorderHabitsRes> r) =>
      (r.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode;

    var ok = await controller.Reorder(owner, new ReorderHabitsReq([b.ToString(), c.ToString()]));
    ok.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>()
      .Which.Value.Should().BeOfType<ReorderHabitsRes>()
      .Which.HabitIds.Should().Equal(b.ToString(), c.ToString(), a.ToString());

    Status(await controller.Reorder(stranger, new ReorderHabitsReq([a.ToString()]))).Should().Be(403);
    Status(await controller.Reorder(owner, new ReorderHabitsReq(["not-a-guid"]))).Should().Be(400);
    Status(await controller.Reorder(owner, new ReorderHabitsReq([a.ToString(), a.ToString()]))).Should().Be(400);

    (await SearchIds(owner)).Should().Equal(b, c, a);
  }
}
