using FluentValidation;

namespace App.Modules.NfcTag.API.V1;

public class LinkNfcTagReqValidator : AbstractValidator<LinkNfcTagReq>
{
  public LinkNfcTagReqValidator()
  {
    RuleFor(x => x.HabitId).NotEmpty();
  }
}
