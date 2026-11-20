namespace Domain.Friend;

public enum FriendRequestError
{
  IdRequired,
  Resolved,
  WrongRole,
}

public class FriendRequestException(FriendRequestError error) : Exception(error switch
{
  FriendRequestError.IdRequired => "Update LazyTax to respond to this friend request.",
  FriendRequestError.Resolved => "This friend request has already been resolved.",
  _ => "You cannot make this decision on this friend request.",
})
{
  public FriendRequestError Error { get; } = error;
}
