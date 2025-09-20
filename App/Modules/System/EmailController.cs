using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Email;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.StartUp.Smtp;
using Asp.Versioning;
using CSharp_Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.System;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class EmailController(IEmailRenderer renderer, ISmtpClientFactory factory, IAuthHelper h): AtomiControllerBase(h)
{
  [HttpPost("{to}")]
  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  public async Task<ActionResult<object>> TestEmail(string to)
  {
    var smtp = factory.Get(SmtpProviders.Transactional);
    var email = await renderer.RenderEmail("member-thank-you", new
    {
      BaseUrl = "http://localhost:5000",
      UserName = to.Split("@")[0],
      UserEmail = to,
      SupportEmail = "support@lazytax.club",
      WhatsappUrl = "https://wa.me/6281234567890",
      TelegramUrl = "https://t.me/lazytax",
      MemberSince = "2025",
      MembershipType = "Platinum",
    })
    .ThenAwait(async x => await smtp.SendAsync(new SmtpEmailMessage
    {
      To = to,
      Subject = "Welcome To LazyTax Club!",
      Body = x,
      IsHtml = true,
    }));
    return this.ReturnUnitResult(email);

  }
}
