using App.Error;
using App.Error.V1;
using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.NfcTag.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Habit;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.NfcTag;

// DB-backed integration tests for the NfcTag mapping against a REAL Postgres
// MainDbContext: claim / remap / cross-user conflict on the repository's own
// read-modify-write path, execution ordering across versions (Postgres NULLS
// FIRST on DESC), owner-only delete, and the habit soft-delete releasing tags.
//
// Connection comes from the NFC_TAG_TEST_DB env var (same format as
// PENALTY_TEST_DB, e.g. "Host=localhost;Port=5432;Database=nfc_test;
// Username=postgres;Password=postgres;"). Unset -> tests Skip, so the suite
// stays green without a database.
[Collection(DbIntegrationCollection.Name)]
public class NfcTagIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "NFC_TAG_TEST_DB not set; skipping DB-backed NFC tag integration test.";
  private readonly string? _conn = Environment.GetEnvironmentVariable("NFC_TAG_TEST_DB");
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
      Database = parts.GetValueOrDefault("database", "nfc_test"),
      User = parts.GetValueOrDefault("username", parts.GetValueOrDefault("user", "postgres")),
      Password = parts.GetValueOrDefault("password", "postgres"),
      AutoMigrate = false,
      Timeout = 30
    };
    var monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    return new MainDbContext(monitor, NullLoggerFactory.Instance);
  }

  private NfcTagRepository Repo() => new(_db, NullLogger<NfcTagRepository>.Instance);

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<(Guid HabitId, Guid VersionId)> SeedHabit(string userId)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();
    var charityId = Guid.NewGuid();
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = versionId,
      HabitId = habitId,
      CharityId = charityId,
      Version = 1,
      Task = $"task-{versionId:N}",
      DaysOfWeek = ["Monday"],
      Timezone = "Asia/Singapore"
    });
    await _db.SaveChangesAsync();
    return (habitId, versionId);
  }

  private static string NewTagId() => Guid.NewGuid().ToString();

  [SkippableFact]
  public async Task Upsert_ClaimRemapAndCrossUserConflict()
  {
    Skip.If(_conn == null, SkipReason);
    var repo = Repo();
    var owner = await SeedUser();
    var stranger = await SeedUser();
    var (habitA, _) = await SeedHabit(owner);
    var (habitB, _) = await SeedHabit(owner);
    var (habitS, _) = await SeedHabit(stranger);
    var tagId = NewTagId();

    // Claim
    var claimed = await repo.Upsert(tagId, owner, habitA);
    claimed.IsSuccess().Should().BeTrue();

    // Remap (same owner) keeps one row, updates the habit
    var remapped = await repo.Upsert(tagId, owner, habitB);
    remapped.IsSuccess().Should().BeTrue();
    var row = await _db.NfcTags.AsNoTracking().SingleAsync(x => x.Id == tagId);
    row.HabitId.Should().Be(habitB);
    row.UserId.Should().Be(owner);

    // Cross-user upsert hits the repository's own ownership guard (the
    // service-level check is bypassed here on purpose - defense in depth)
    var hijack = await repo.Upsert(tagId, stranger, habitS);
    var error = hijack.FailureOrDefault();
    error.Should().BeOfType<DomainProblemException>();
    ((DomainProblemException)error).Problem.Should().BeOfType<EntityConflict>();
    (await _db.NfcTags.AsNoTracking().SingleAsync(x => x.Id == tagId))
      .UserId.Should().Be(owner, "the original owner keeps the tag");
  }

  [SkippableFact]
  public async Task GetExecutionForDate_CompletionWinsOverNullCompletedAt_AcrossVersions()
  {
    Skip.If(_conn == null, SkipReason);
    var repo = Repo();
    var owner = await SeedUser();
    var (habitId, v1) = await SeedHabit(owner);

    // Second version of the SAME habit (edit): unique(version, habit) per schema
    var v2 = Guid.NewGuid();
    var charityId = Guid.NewGuid();
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = v2,
      HabitId = habitId,
      CharityId = charityId,
      Version = 2,
      Task = "task-v2",
      DaysOfWeek = ["Monday"],
      Timezone = "Asia/Singapore"
    });

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    // v1: a Vacation row with NULL CompletedAt; v2: a real completion.
    // Postgres sorts NULLS FIRST on DESC - the coalesced ordering must still
    // surface the completion.
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = Guid.NewGuid(),
      HabitVersionId = v1,
      Date = today,
      Status = HabitExecutionStatusData.Vacation
    });
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = Guid.NewGuid(),
      HabitVersionId = v2,
      Date = today,
      Status = HabitExecutionStatusData.Completed,
      CompletedAt = DateTime.UtcNow
    });
    await _db.SaveChangesAsync();

    var result = await repo.GetExecutionForDate(habitId, today);

    result.IsSuccess().Should().BeTrue();
    var execution = (HabitExecutionPrincipal?)result;
    execution.Should().NotBeNull();
    execution!.Record.Status.Should().Be(ExecutionStatus.Completed);
  }

  [SkippableFact]
  public async Task Delete_IsOwnerOnly()
  {
    Skip.If(_conn == null, SkipReason);
    var repo = Repo();
    var owner = await SeedUser();
    var stranger = await SeedUser();
    var (habitId, _) = await SeedHabit(owner);
    var tagId = NewTagId();
    (await repo.Upsert(tagId, owner, habitId)).IsSuccess().Should().BeTrue();

    var strangerDelete = await repo.Delete(tagId, stranger);
    ((Unit?)strangerDelete).Should().BeNull("someone else's unlink must not delete");
    (await _db.NfcTags.AsNoTracking().CountAsync(x => x.Id == tagId)).Should().Be(1);

    var ownerDelete = await repo.Delete(tagId, owner);
    ((Unit?)ownerDelete).Should().NotBeNull();
    (await _db.NfcTags.AsNoTracking().CountAsync(x => x.Id == tagId)).Should().Be(0);
  }

  [SkippableFact]
  public async Task HabitSoftDelete_ReleasesTheTag()
  {
    Skip.If(_conn == null, SkipReason);
    var nfcRepo = Repo();
    var habitRepo = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);
    var owner = await SeedUser();
    var (habitId, _) = await SeedHabit(owner);
    var tagId = NewTagId();
    (await nfcRepo.Upsert(tagId, owner, habitId)).IsSuccess().Should().BeTrue();

    var deleted = await habitRepo.Delete(habitId, owner);

    ((Unit?)deleted).Should().NotBeNull();
    (await _db.Habits.AsNoTracking().SingleAsync(x => x.Id == habitId))
      .DeletedAt.Should().NotBeNull("habit deletion is a soft delete");
    (await _db.NfcTags.AsNoTracking().AnyAsync(x => x.Id == tagId))
      .Should().BeFalse("the tag must be released so the sticker is reusable");
  }
}
