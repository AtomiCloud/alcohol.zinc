using CSharp_Result;
using Domain.Exceptions;
using Domain.Friend;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Friend;

public class FriendTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task PairOnlyActions_RequireAnUpdate_WithoutTouchingDependencies(bool accept)
  {
    var service = new FriendService(null!, null!, NullLogger<FriendService>.Instance);
    var failure = accept
      ? (await service.Accept("recipient", "sender")).FailureOrDefault()
      : (await service.DeleteRequest("recipient", "sender")).FailureOrDefault();
    failure.Should().BeOfType<FriendRequestException>().Which.Error.Should().Be(FriendRequestError.IdRequired);
    failure.Message.Should().Be("Update LazyTax to respond to this friend request.");
  }

  [Theory]
  [InlineData("")]
  [InlineData("ACCEPT")]
  [InlineData("accepted")]
  [InlineData(" decline")]
  [InlineData(null)]
  public async Task Resolve_RejectsUnknownDecisions(string? decision)
  {
    var service = new FriendService(null!, null!, NullLogger<FriendService>.Instance);
    var result = await service.ResolveRequest("recipient", Guid.NewGuid(), decision!);
    result.FailureOrDefault().Should().BeOfType<InvalidRequestException>();
  }
}
