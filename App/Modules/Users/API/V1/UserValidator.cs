using App.Utility;
using FluentValidation;

namespace App.Modules.Users.API.V1;

public class CreateUserReqValidator : AbstractValidator<CreateUserReq>
{
  public CreateUserReqValidator()
  {
    this.RuleFor(x => x.IdToken)
      .NotNull();
    this.RuleFor(x => x.AccessToken)
      .NotNull();
  }
}

public class UpdateUserReqValidator : AbstractValidator<UpdateUserReq>
{
  public UpdateUserReqValidator()
  {
    this.RuleFor(x => x.IdToken)
      .NotNull();
    this.RuleFor(x => x.AccessToken)
      .NotNull();
  }
}

public class UserSearchQueryValidator : AbstractValidator<SearchUserQuery>
{
  public UserSearchQueryValidator()
  {
    this.RuleFor(x => x.Id)
      .MinimumLength(1)
      .Unless(x => x.Id == null);
    this.RuleFor(x => x.Username)
      .MinimumLength(1)
      .Unless(x => x.Username == null);
    this.RuleFor(x => x.Email)
      .MinimumLength(1)
      .Unless(x => x.Email == null);
    this.RuleFor(x => x.Limit)
      .Limit();
    this.RuleFor(x => x.Skip)
      .Skip();
  }
}
