using App.Error;
using App.Error.V1;
using App.Modules.NfcTag;
using App.Utility;
using Domain.Exceptions;
using Domain.Habit;
using Domain.NfcTag;
using NodaMoney;

namespace UnitTest.NfcTag;

// NfcTagService: link (claim / re-link / FCFS conflict / unknown habit),
// resolve (owner-only, no ownership leak, today's execution), and unlink.
public class NfcTagServiceTests
{
  private const string Owner = "user-1";
  private const string Stranger = "user-2";
  private const string TagId = "3f9c2b7e-1a2b-4c3d-8e9f-0a1b2c3d4e5f";

  private static readonly Guid HabitA = Guid.NewGuid();
  private static readonly Guid HabitB = Guid.NewGuid();

  private static HabitVersionPrincipal Version(Guid habitId, string timezone = "Asia/Singapore") => new()
  {
    Id = Guid.NewGuid(),
    HabitId = habitId,
    Version = 1,
    Record = new HabitVersionRecord
    {
      CharityId = Guid.NewGuid(),
      Task = "Eat medicine",
      DaysOfWeek = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"],
      NotificationTime = new TimeOnly(21, 0),
      Stake = new Money(5m, Currency.FromCode("USD")),
      Ratio = 1.0m,
      Timezone = timezone
    }
  };

  private static (NfcTagService Svc, FakeNfcTagRepository Repo, FakeHabitService Habits) Make()
  {
    var repo = new FakeNfcTagRepository();
    var habits = new FakeHabitService();
    return (new NfcTagService(repo, habits), repo, habits);
  }

  [Fact]
  public async Task Link_UnclaimedTag_CreatesMapping()
  {
    var (svc, repo, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);

    var result = await svc.Link(Owner, TagId, HabitA);

    result.IsSuccess().Should().BeTrue();
    repo.Tags[TagId].UserId.Should().Be(Owner);
    repo.Tags[TagId].Record.HabitId.Should().Be(HabitA);
  }

  [Fact]
  public async Task Link_OwnTag_RemapsToNewHabit()
  {
    var (svc, repo, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    habits.Versions[(Owner, HabitB)] = Version(HabitB);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Link(Owner, TagId, HabitB);

    result.IsSuccess().Should().BeTrue();
    repo.Tags[TagId].Record.HabitId.Should().Be(HabitB);
    repo.Tags.Should().HaveCount(1);
  }

  [Fact]
  public async Task Link_TagOwnedByAnotherUser_FailsWithEntityConflict()
  {
    var (svc, repo, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    habits.Versions[(Stranger, HabitB)] = Version(HabitB);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Link(Stranger, TagId, HabitB);

    var error = result.FailureOrDefault();
    error.Should().BeOfType<DomainProblemException>();
    ((DomainProblemException)error).Problem.Should().BeOfType<EntityConflict>();
    repo.Tags[TagId].UserId.Should().Be(Owner, "the original owner keeps the tag");
  }

  [Fact]
  public async Task Link_HabitNotOwnedOrMissing_FailsWithNotFound()
  {
    var (svc, _, _) = Make();

    var result = await svc.Link(Owner, TagId, HabitA);

    result.FailureOrDefault().Should().BeOfType<NotFoundException>();
  }

  [Fact]
  public async Task Resolve_OwnTag_ReturnsVersionAndTodayExecution()
  {
    var (svc, repo, habits) = Make();
    var version = Version(HabitA);
    habits.Versions[(Owner, HabitA)] = version;
    await svc.Link(Owner, TagId, HabitA);

    var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore");
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    var execution = new HabitExecutionPrincipal
    {
      Id = Guid.NewGuid(),
      HabitVersionId = version.Id,
      Record = new HabitExecutionRecord { Date = today, Status = ExecutionStatus.Completed, CompletedAt = DateTime.UtcNow }
    };
    repo.Executions[(HabitA, today)] = execution;

    var result = await svc.Resolve(Owner, TagId);

    result.IsSuccess().Should().BeTrue();
    var resolution = (NfcTagResolution?)result;
    resolution.Should().NotBeNull();
    resolution!.HabitVersion.Id.Should().Be(version.Id);
    resolution.Today.Should().Be(today);
    resolution.TodayExecution.Should().NotBeNull();
    resolution.TodayExecution!.Record.Status.Should().Be(ExecutionStatus.Completed);
  }

  [Fact]
  public async Task Resolve_NotActedOnToday_ReturnsNullExecution()
  {
    var (svc, _, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Resolve(Owner, TagId);

    result.IsSuccess().Should().BeTrue();
    var resolution = (NfcTagResolution?)result;
    resolution.Should().NotBeNull();
    resolution!.TodayExecution.Should().BeNull();
  }

  [Fact]
  public async Task Resolve_HabitDeletedAfterLinking_ReturnsNull()
  {
    var (svc, _, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    await svc.Link(Owner, TagId, HabitA);
    habits.Versions.Remove((Owner, HabitA));

    var result = await svc.Resolve(Owner, TagId);

    result.IsSuccess().Should().BeTrue();
    ((NfcTagResolution?)result).Should().BeNull("a dangling tag must resolve like an unclaimed one");
  }

  [Fact]
  public async Task Resolve_UnclaimedTag_ReturnsNull()
  {
    var (svc, _, _) = Make();

    var result = await svc.Resolve(Owner, TagId);

    result.IsSuccess().Should().BeTrue();
    ((NfcTagResolution?)result).Should().BeNull();
  }

  [Fact]
  public async Task Resolve_TagOwnedByAnotherUser_ReturnsNullWithoutLeakingOwnership()
  {
    var (svc, _, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Resolve(Stranger, TagId);

    result.IsSuccess().Should().BeTrue("foreign tags must look identical to unclaimed ones");
    ((NfcTagResolution?)result).Should().BeNull();
  }

  [Fact]
  public async Task Unlink_OwnTag_RemovesMapping()
  {
    var (svc, repo, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Unlink(Owner, TagId);

    result.IsSuccess().Should().BeTrue();
    ((CSharp_Result.Unit?)result).Should().NotBeNull();
    repo.Tags.Should().BeEmpty();
  }

  [Fact]
  public async Task Unlink_TagOwnedByAnotherUser_ReturnsNullAndKeepsMapping()
  {
    var (svc, repo, habits) = Make();
    habits.Versions[(Owner, HabitA)] = Version(HabitA);
    await svc.Link(Owner, TagId, HabitA);

    var result = await svc.Unlink(Stranger, TagId);

    result.IsSuccess().Should().BeTrue();
    ((CSharp_Result.Unit?)result).Should().BeNull();
    repo.Tags.Should().ContainKey(TagId);
  }
}
