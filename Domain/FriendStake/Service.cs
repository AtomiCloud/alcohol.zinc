using CSharp_Result;
using Domain.Exceptions;
using Domain.Friend;
using Domain.Habit;

namespace Domain.FriendStake;

public class FriendStakeService(
  IFriendStakeRepository repo,
  IHabitRepository habitRepo,
  IFriendService friends
) : IFriendStakeService
{
  public const int MaxWatchers = 20;

  public async Task<Result<List<FriendUser>>> GetWatchers(string userId, Guid habitId)
  {
    var owned = await EnsureOwned(userId, habitId);
    if (owned.IsFailure()) return owned.FailureOrDefault();
    return await repo.GetWatchers(habitId);
  }

  public async Task<Result<List<FriendUser>>> SetWatchers(string userId, Guid habitId,
    IReadOnlyList<string> friendUserIds)
  {
    var owned = await EnsureOwned(userId, habitId);
    if (owned.IsFailure()) return owned.FailureOrDefault();

    var ids = friendUserIds.Distinct().ToList();
    if (ids.Count > MaxWatchers)
      return new InvalidRequestException("friendUserIds", $"At most {MaxWatchers} friends can be told");

    var notFriends = new List<string>();
    foreach (var id in ids)
    {
      var isFriend = await friends.AreFriends(userId, id);
      if (isFriend.IsFailure()) return isFriend.FailureOrDefault();
      if (!isFriend.Get()) notFriends.Add(id);
    }

    if (notFriends.Count > 0)
      return new InvalidRequestException("friendUserIds", $"Not your friends: {string.Join(", ", notFriends)}");

    return await repo.ReplaceWatchers(habitId, ids);
  }

  private async Task<Result<Unit>> EnsureOwned(string userId, Guid habitId)
  {
    var habit = (HabitVersionPrincipal?)await habitRepo.GetCurrentVersion(userId, habitId);
    if (habit == null) return new NotFoundException("Habit Not Found", typeof(HabitPrincipal), habitId.ToString());
    return new Unit();
  }
}
