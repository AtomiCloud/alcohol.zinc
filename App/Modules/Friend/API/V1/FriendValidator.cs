using FluentValidation;

namespace App.Modules.Friend.API.V1;

public class SendFriendRequestReqValidator : AbstractValidator<SendFriendRequestReq>
{
  public SendFriendRequestReqValidator()
  {
    RuleFor(x => x.ToUserId).NotEmpty().MaximumLength(128);
  }
}
