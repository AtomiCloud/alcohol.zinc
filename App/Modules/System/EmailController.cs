using System.Net.Mime;
using App.Modules.Common;
using App.Modules.Notification;
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
public class EmailController(IEmailRenderer renderer, ISmtpClientFactory factory, IAuthHelper h) : AtomiControllerBase(h)
{
  private const string BaseUrl = "https://lazytax.club";
  private const string SupportEmail = EmailNotifier.SupportEmail;

  // Sample payloads per template id, for inbox-rendering verification across
  // real email clients. Vars mirror what EmailNotifier composes in production.
  private static readonly Dictionary<string, (string Subject, Func<string, object> Vars)> Samples = new()
  {
    [EmailTemplates.PenaltyCharged] = (EmailTemplates.PenaltyChargedSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      Amount = "$5.00",
      CharityName = "Doctors Without Borders",
      ChargeDate = "12 July 2026",
    }),
    [EmailTemplates.PenaltyPaymentFailed] = (EmailTemplates.PenaltyPaymentFailedSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      Amount = "$5.00",
      CharityName = "Doctors Without Borders",
      AttemptDate = "12 July 2026",
    }),
    [EmailTemplates.SubscriptionReceipt] = (EmailTemplates.SubscriptionReceiptSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      Tier = "Pro",
      Amount = "$4.99",
      ChargeDate = "12 July 2026",
      NextBillingDate = "12 August 2026",
    }),
    [EmailTemplates.SubscriptionPaymentFailed] = (EmailTemplates.SubscriptionPaymentFailedSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      Tier = "Pro",
      Amount = "$4.99",
      GraceEndsDate = "19 July 2026",
    }),
    [EmailTemplates.SubscriptionCancelled] = (EmailTemplates.SubscriptionCancelledSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      ChangeSummary = "Pro → Free",
      EffectiveDate = "12 August 2026",
    }),
    [EmailTemplates.Welcome] = (EmailTemplates.WelcomeSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
    }),
    [EmailTemplates.PaymentConsentChanged] = (EmailTemplates.PaymentConsentChangedSubject, to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      SupportEmail,
      ChangeSummary = "Payment method linked",
      Purpose = "Habit stakes",
      ChangeDate = "12 July 2026",
    }),
    [EmailTemplates.MemberThankYou] = ("Thank you for being a LazyTax member", to => new
    {
      BaseUrl,
      UserName = to.Split("@")[0],
      UserEmail = to,
      SupportEmail,
      WhatsappUrl = "https://wa.me/message/BXGMZ4HV5M32K1",
      TelegramUrl = "https://t.me/lazytax",
      MemberSince = "January 2026",
      MembershipType = "Pro",
    }),
  };

  [HttpPost("{template}/{to}")]
  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  public async Task<ActionResult<object>> TestEmail(string template, string to)
  {
    if (!Samples.TryGetValue(template, out var sample))
      return this.NotFound(new { error = $"Unknown template '{template}'", known = Samples.Keys });

    var smtp = factory.Get(SmtpProviders.Transactional);
    var email = await renderer.RenderEmail(template, sample.Vars(to))
      .ThenAwait(async x => await smtp.SendAsync(new SmtpEmailMessage
      {
        To = to,
        Subject = sample.Subject,
        Body = x,
        IsHtml = true,
      }));
    return this.ReturnUnitResult(email);
  }
}
