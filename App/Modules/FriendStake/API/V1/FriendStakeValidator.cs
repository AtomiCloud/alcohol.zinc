using Domain.FriendStake;
using FluentValidation;

namespace App.Modules.FriendStake.API.V1;

public class SetFriendWatchersReqValidator : AbstractValidator<SetFriendWatchersReq>
{
  public SetFriendWatchersReqValidator()
  {
    RuleFor(x => x.FriendUserIds).NotNull()
      .Must(ids => ids == null || ids.Distinct().Count() <= FriendStakeService.MaxWatchers)
      .WithMessage($"At most {FriendStakeService.MaxWatchers} friends can be told");
    RuleForEach(x => x.FriendUserIds).NotEmpty().MaximumLength(128);
  }
}
