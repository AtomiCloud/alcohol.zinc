using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Protection;

public class HabitOrderTests
{
  private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

  [Fact]
  public void Merge_FullList_ReturnsRequestedOrder()
  {
    HabitOrder.Merge([A, B, C], [C, A, B]).Get().Should().Equal(C, A, B);
  }

  [Fact]
  public void Merge_PartialList_AppendsMissingInCurrentOrder()
  {
    HabitOrder.Merge([A, B, C, D], [D, B]).Get().Should().Equal(D, B, A, C);
  }

  [Fact]
  public void Merge_EmptyRequest_KeepsCurrentOrder()
  {
    HabitOrder.Merge([B, A, C], []).Get().Should().Equal(B, A, C);
  }

  [Fact]
  public void Merge_DuplicateId_IsInvalidRequest()
  {
    var result = HabitOrder.Merge([A, B, C], [A, A, B]);

    result.FailureOrDefault().Should().BeOfType<InvalidRequestException>()
      .Which.Errors.Should().ContainKey("habitIds");
  }

  [Fact]
  public void Merge_UnknownId_IsInvalidRequest()
  {
    var result = HabitOrder.Merge([A, B, C], [A, Guid.NewGuid()]);

    result.FailureOrDefault().Should().BeOfType<InvalidRequestException>()
      .Which.Errors.Should().ContainKey("habitIds");
  }

  [Fact]
  public async Task Reorder_ValidList_WritesMergedOrderAndReturnsIt()
  {
    var (service, repo) = Service();

    var result = await service.Reorder("owner", [D, B]);

    result.Get().Should().Equal(D, B, A, C);
    var (userId, orderedIds) = repo.SetOrderCalls.Should().ContainSingle().Subject;
    userId.Should().Be("owner");
    orderedIds.Should().Equal(D, B, A, C);
    (await repo.GetOrderedIds("owner")).Get().Should().Equal(D, B, A, C);
  }

  [Fact]
  public async Task Reorder_InvalidList_WritesNothing()
  {
    var (service, repo) = Service();
    var strangers = Guid.NewGuid();
    repo.Habits.Add(HabitFakeFactory.Habit(strangers, "stranger"));

    (await service.Reorder("owner", [C, C])).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.Reorder("owner", [C, strangers])).FailureOrDefault().Should().BeOfType<InvalidRequestException>();

    repo.SetOrderCalls.Should().BeEmpty();
    (await repo.GetOrderedIds("owner")).Get().Should().Equal(A, B, C, D);
  }

  // The owner's habits A, B, C, D at positions 0..3.
  private static (HabitService, FakeHabitRepository) Service()
  {
    var repo = new FakeHabitRepository();
    Guid[] ids = [A, B, C, D];
    for (var i = 0; i < ids.Length; i++) repo.Habits.Add(HabitFakeFactory.Habit(ids[i], "owner", sortOrder: i));
    var service = new HabitService(repo, new FakeEntitlementService(), new FakeCheckService(), null!,
      NullLogger<HabitService>.Instance);
    return (service, repo);
  }
}
