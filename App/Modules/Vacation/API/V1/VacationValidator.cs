using App.Utility;
using FluentValidation;

namespace App.Modules.Vacation.API.V1;

public class CreateVacationReqValidator : AbstractValidator<CreateVacationReq>
{
  public CreateVacationReqValidator()
  {
    RuleFor(x => x.StartDate).NotEmpty().DateValid();
    RuleFor(x => x.EndDate).NotEmpty().DateValid();
    RuleFor(x => x.Timezone).NotEmpty().TimezoneValid();
  }
}

public class SearchVacationQueryValidator : AbstractValidator<SearchVacationQuery>
{
  public SearchVacationQueryValidator()
  {
    When(x => x.Limit != null, () => RuleFor(x => x.Limit).Limit());
    When(x => x.Skip != null, () => RuleFor(x => x.Skip).Skip());
  }
}

