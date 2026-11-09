using App.Utility;
using FluentValidation;

namespace App.Modules.Routine.API.V1;

public class CreateRoutineReqValidator : AbstractValidator<CreateRoutineReq>
{
  public CreateRoutineReqValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
  }
}

public class UpdateRoutineReqValidator : AbstractValidator<UpdateRoutineReq>
{
  public UpdateRoutineReqValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
  }
}

public class SearchRoutineQueryValidator : AbstractValidator<SearchRoutineQuery>
{
  public SearchRoutineQueryValidator()
  {
    When(x => x.Limit != null, () => RuleFor(x => x.Limit).Limit());
    When(x => x.Skip != null, () => RuleFor(x => x.Skip).Skip());
  }
}
