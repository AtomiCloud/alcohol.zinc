using CSharp_Result;
using Domain.Exceptions;

namespace Domain.Notification;

public class InboxService(IInboxRepository repo) : IInboxService
{
  // Mirror the column lengths of InboxNotificationData.
  public const int TitleMaxLength = 256;
  public const int BodyMaxLength = 1024;
  public const int MaxLimit = 50;

  public Task<Result<InboxNotificationPrincipal>> Post(string userId, InboxNotificationRecord record)
  {
    var errors = new Dictionary<string, string[]>();
    CheckText(errors, "title", record.Title, TitleMaxLength);
    CheckText(errors, "body", record.Body, BodyMaxLength);
    if (errors.Count > 0)
      return Task.FromResult<Result<InboxNotificationPrincipal>>(new InvalidRequestException(errors));
    return repo.Create(userId, record);
  }

  public Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search)
  {
    var errors = new Dictionary<string, string[]>();
    if (search.Limit is < 1 or > MaxLimit) errors["limit"] = [$"Limit must be between 1 and {MaxLimit}"];
    if (search.Skip < 0) errors["skip"] = ["Skip must not be negative"];
    if (errors.Count > 0)
      return Task.FromResult<Result<List<InboxNotificationPrincipal>>>(new InvalidRequestException(errors));
    return repo.Search(search);
  }

  public Task<Result<int>> UnreadCount(string userId) => repo.UnreadCount(userId);

  public Task<Result<Unit?>> MarkRead(string userId, Guid id) => repo.MarkRead(userId, id, DateTime.UtcNow);

  public Task<Result<int>> MarkAllRead(string userId) => repo.MarkAllRead(userId, DateTime.UtcNow);

  private static void CheckText(Dictionary<string, string[]> errors, string field, string? value, int max)
  {
    if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{field} must not be empty"];
    else if (value.Length > max) errors[field] = [$"{field} must be at most {max} characters"];
  }
}
