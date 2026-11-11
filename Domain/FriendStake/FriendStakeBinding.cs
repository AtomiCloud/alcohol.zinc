using System.Globalization;
using System.Text.Json;
using CSharp_Result;
using Domain.Check;
using Domain.Friend;
using Domain.Habit;
using Domain.Notification;
using Domain.User;
using Microsoft.Extensions.Logging;

namespace Domain.FriendStake;

// "Tell my friends if I miss": a missed check posts one friend_missed notification to each
// friend watching the habit. Friends only learn that it was missed, never proof details.
//
// Safe to retry: every post carries DedupeKey friend_missed:{checkId}, so a watcher already
// told is not told again. A post that fails for one watcher doesn't stop the others; once all
// were tried, Deliver fails so the outbox retries the event and only the failed ones get it.
public class FriendStakeBinding(
  IFriendStakeRepository repo,
  ICheckRepository checkRepo,
  IHabitRepository habitRepo,
  IUserRepository userRepo,
  IInboxService inbox,
  ILogger<FriendStakeBinding> logger
) : IStakeBinding
{
  private const int MaxTaskLength = 200;
  private const int MaxTitleLength = 256;

  public string BindingId => "friends";

  public bool AppliesTo(CheckEventPrincipal e) => e.Outcome == CheckStatus.Missed && e.ExecutionId != null;

  public async Task<Result<Unit>> Deliver(CheckEventPrincipal e)
  {
    var watchers = (List<FriendUser>)await repo.GetWatchers(e.HabitId);
    if (watchers.Count == 0) return new Unit();

    var check = (CheckPrincipal?)await checkRepo.Get(e.CheckId);
    var task = (string?)await habitRepo.GetTaskNameByExecutionId(e.ExecutionId!.Value);
    var owner = (await userRepo.GetById(e.UserId)).Get();
    // The check, its execution or the owner is gone: nothing left to tell.
    if (check == null || task == null || owner == null) return new Unit();

    var username = owner.Principal.Record.Username;
    var date = check.Record.LocalDate;
    if (task.Length > MaxTaskLength) task = task[..(MaxTaskLength - 1)] + "…";
    var title = $"{username} missed a habit";
    var record = new InboxNotificationRecord
    {
      Kind = InboxKinds.FriendMissed,
      Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title,
      Body = $"{username} missed \"{task}\" on {date.ToString("ddd d MMM", CultureInfo.InvariantCulture)}",
      ActorUserId = e.UserId,
      Data = JsonSerializer.Serialize(new
      {
        habitId = e.HabitId,
        userId = e.UserId,
        localDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
      }),
      DedupeKey = $"friend_missed:{e.CheckId}"
    };

    var failed = new List<string>();
    foreach (var w in watchers)
    {
      try
      {
        var posted = await inbox.Post(w.UserId, record);
        if (posted.IsSuccess()) continue;
        logger.LogError(posted.FailureOrDefault(), "friend_missed post failed for CheckId={CheckId} Watcher={Watcher}",
          e.CheckId, w.UserId);
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "friend_missed post threw for CheckId={CheckId} Watcher={Watcher}", e.CheckId, w.UserId);
      }

      failed.Add(w.UserId);
    }

    if (failed.Count > 0)
      return new Exception(
        $"friend_missed not posted to {failed.Count} of {watchers.Count} watchers: {string.Join(", ", failed)}");
    return new Unit();
  }
}
