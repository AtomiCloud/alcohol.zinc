using CSharp_Result;
using Domain.Friend;

namespace Domain.FriendStake;

// "Tell my friends if I miss": the friends told when a habit's day ends Missed.
public interface IFriendStakeService
{
  Task<Result<List<FriendUser>>> GetWatchers(string userId, Guid habitId);

  // Replaces the habit's watchers. Each must be a friend of the user; an empty list turns it off.
  Task<Result<List<FriendUser>>> SetWatchers(string userId, Guid habitId, IReadOnlyList<string> friendUserIds);
}

public interface IFriendStakeRepository
{
  Task<Result<List<FriendUser>>> GetWatchers(Guid habitId);
  Task<Result<List<FriendUser>>> ReplaceWatchers(Guid habitId, IReadOnlyList<string> friendUserIds);
}
