using App.Modules.Notification;
using App.StartUp.Email;
using CSharp_Result;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Notification;

// Renders every registered template from the embedded resources with a full
// variable set. Guards the whole pipeline: `task email:build` ran before
// `dotnet build` (resource exists), Handlebars tokens fill, and no `{{`
// residue leaks to users.
public class EmailTemplateRenderTests
{
  private static readonly object CommonVars = new
  {
    BaseUrl = "https://lazytax.club",
    UserName = "Sarah",
    UserEmail = "sarah@example.com",
    SupportEmail = "support@lazytax.club",
    Amount = "$5.00",
    CharityName = "Doctors Without Borders",
    HabitName = "Morning run",
    ChargeDate = "12 July 2026",
    AttemptDate = "12 July 2026",
    Tier = "Pro",
    NextBillingDate = "12 August 2026",
    GraceEndsDate = "19 July 2026",
    ChangeSummary = "Pro → Free",
    EffectiveDate = "12 August 2026",
    Purpose = "Habit stakes",
    ChangeDate = "12 July 2026",
    WhatsappUrl = "https://wa.me/message/BXGMZ4HV5M32K1",
    TelegramUrl = "https://t.me/lazytax",
    MemberSince = "January 2026",
    MembershipType = "Pro",
  };

  [Theory]
  [InlineData(EmailTemplates.PenaltyCharged)]
  [InlineData(EmailTemplates.PenaltyPaymentFailed)]
  [InlineData(EmailTemplates.SubscriptionReceipt)]
  [InlineData(EmailTemplates.SubscriptionPaymentFailed)]
  [InlineData(EmailTemplates.SubscriptionCancelled)]
  [InlineData(EmailTemplates.Welcome)]
  [InlineData(EmailTemplates.PaymentConsentChanged)]
  [InlineData(EmailTemplates.MemberThankYou)]
  public async Task Template_RendersFromEmbeddedResource_WithNoTokenResidue(string templateId)
  {
    var renderer = new EmailRenderer(NullLogger<EmailRenderer>.Instance);

    var res = await renderer.RenderEmail(templateId, CommonVars);

    res.IsSuccess().Should().BeTrue(
      $"template '{templateId}' must exist as an embedded resource — did `task email:build` run before `dotnet build`?");
    var html = res.Get();
    html.Should().Contain("Sarah");
    html.Should().Contain("support@lazytax.club");
    html.Should().NotContain("{{", "every Handlebars token must be filled");
    html.Should().Contain("lazytax.club/images/email/logo.png", "the header must hotlink the argon logo PNG");
  }
}
