namespace Domain.Exceptions;

// Another request of the same user held the user's lock for too long; the caller can retry.
public class UserBusyException(string userId) : Exception($"Another request of user {userId} is in progress; retry shortly")
{
  public string UserId { get; } = userId;
}
