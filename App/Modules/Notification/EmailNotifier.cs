using System.Globalization;
using App.StartUp.Email;
using App.StartUp.Options;
using App.StartUp.Registry;
using App.StartUp.Smtp;
using CSharp_Result;
using Domain.Charity;
using Domain.Habit;
using Domain.Notification;
using Domain.Subscription;
using Domain.User;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace App.Modules.Notification;

public class EmailNotifier(
  IEmailRenderer renderer,
  ISmtpClientFactory factory,
  IUserRepository users,
  ICharityService charities,
  IHabitRepository habits,
  ISubscriptionPlanProvider plans,
  IOptionsMonitor<WebPortalOption> portal,
  ILogger<EmailNotifier> logger
) : IEmailNotifier
{
  public const string SupportEmail = "support@lazytax.club";

  public async Task NotifyPenaltyCharged(string userId, Money amount, Guid charityId, Guid habitExecutionId,
    DateTime chargedAtUtc)
  {
    await this.SendSafe(userId, EmailTemplates.PenaltyCharged, EmailTemplates.PenaltyChargedSubject,
      async user => new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        Amount = FormatMoney(amount),
        CharityName = await this.CharityName(charityId),
        HabitName = await this.HabitName(habitExecutionId),
        ChargeDate = FormatDate(chargedAtUtc),
      });
  }

  public async Task NotifyPenaltyFailed(string userId, Money amount, Guid charityId, Guid habitExecutionId,
    DateTime attemptedAtUtc)
  {
    await this.SendSafe(userId, EmailTemplates.PenaltyPaymentFailed, EmailTemplates.PenaltyPaymentFailedSubject,
      async user => new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        Amount = FormatMoney(amount),
        CharityName = await this.CharityName(charityId),
        HabitName = await this.HabitName(habitExecutionId),
        AttemptDate = FormatDate(attemptedAtUtc),
      });
  }

  public async Task NotifySubscriptionPurchased(string userId, string tier, Money amount, DateTime nextBillingUtc)
  {
    await this.SendSafe(userId, EmailTemplates.SubscriptionReceipt, EmailTemplates.SubscriptionReceiptSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        Tier = tier,
        Amount = FormatMoney(amount),
        ChargeDate = FormatDate(DateTime.UtcNow),
        NextBillingDate = FormatDate(nextBillingUtc),
      }));
  }

  public async Task NotifySubscriptionRenewed(string userId, string tier, DateTime nextBillingUtc)
  {
    await this.SendSafe(userId, EmailTemplates.SubscriptionReceipt, EmailTemplates.SubscriptionReceiptSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        Tier = tier,
        Amount = this.PlanPrice(tier),
        ChargeDate = FormatDate(DateTime.UtcNow),
        NextBillingDate = FormatDate(nextBillingUtc),
      }));
  }

  public async Task NotifySubscriptionPaymentFailed(string userId, string tier, DateTime graceEndsUtc)
  {
    await this.SendSafe(userId, EmailTemplates.SubscriptionPaymentFailed,
      EmailTemplates.SubscriptionPaymentFailedSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        Tier = tier,
        Amount = this.PlanPrice(tier),
        GraceEndsDate = FormatDate(graceEndsUtc),
      }));
  }

  public async Task NotifySubscriptionChanged(string userId, string fromTier, string toTier, DateTime effectiveUtc)
  {
    await this.SendSafe(userId, EmailTemplates.SubscriptionCancelled, EmailTemplates.SubscriptionCancelledSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        ChangeSummary = $"{fromTier} → {toTier}",
        EffectiveDate = FormatDate(effectiveUtc),
      }));
  }

  public async Task NotifySubscriptionEnded(string userId, string tier, DateTime endedAtUtc)
  {
    await this.SendSafe(userId, EmailTemplates.SubscriptionCancelled, EmailTemplates.SubscriptionCancelledSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        ChangeSummary = $"{tier} ended (payment not collected)",
        EffectiveDate = FormatDate(endedAtUtc),
      }));
  }

  public async Task NotifyWelcome(string userId, string email, string username)
  {
    await this.SendSafe(userId, EmailTemplates.Welcome, EmailTemplates.WelcomeSubject,
      _ => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = username,
        SupportEmail,
      }), knownEmail: email);
  }

  public async Task NotifyConsentChanged(string userId, bool linked, string purpose, DateTime changedAtUtc)
  {
    await this.SendSafe(userId, EmailTemplates.PaymentConsentChanged, EmailTemplates.PaymentConsentChangedSubject,
      user => Task.FromResult<object>(new
      {
        BaseUrl = this.BaseUrl(),
        UserName = DisplayName(user),
        SupportEmail,
        ChangeSummary = linked ? "Payment method linked" : "Payment method unlinked",
        Purpose = purpose,
        ChangeDate = FormatDate(changedAtUtc),
      }));
  }

  /// <summary>
  /// Resolve recipient, render, send — entirely best-effort. Never throws
  /// (including on cancellation): a failed email must never fail the money
  /// event whose success it reports.
  /// </summary>
  private async Task SendSafe(
    string userId, string templateId, string subject,
    Func<User?, Task<object>> vars, string? knownEmail = null)
  {
    try
    {
      User? user = null;
      var to = knownEmail;
      if (string.IsNullOrWhiteSpace(to))
      {
        var userRes = await users.GetById(userId);
        user = userRes.IsSuccess() ? userRes.Get() : null;
        to = user?.Principal.Record.Email;
      }

      if (string.IsNullOrWhiteSpace(to))
      {
        logger.LogWarning("Email '{Template}' skipped for user {UserId}: no recipient address", templateId, userId);
        return;
      }

      var rendered = await renderer.RenderEmail(templateId, await vars(user));
      if (!rendered.IsSuccess())
      {
        logger.LogError(rendered.FailureOrDefault(),
          "Email '{Template}' render failed for user {UserId}", templateId, userId);
        return;
      }

      var smtp = factory.Get(SmtpProviders.Transactional);
      await smtp.SendAsync(new SmtpEmailMessage
      {
        To = to,
        Subject = subject,
        Body = rendered.Get(),
        IsHtml = true,
      });
      logger.LogInformation("Email '{Template}' sent to user {UserId}", templateId, userId);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Email '{Template}' send failed for user {UserId}", templateId, userId);
    }
  }

  private string BaseUrl()
  {
    var opt = portal.CurrentValue;
    return $"{opt.Scheme}://{opt.Host}";
  }

  private async Task<string> CharityName(Guid charityId)
  {
    var res = await charities.Get(charityId);
    var name = res.IsSuccess() ? res.Get()?.Principal.Record.Name : null;
    return string.IsNullOrWhiteSpace(name) ? "your chosen charity" : name;
  }

  private async Task<string> HabitName(Guid habitExecutionId)
  {
    var res = await habits.GetTaskNameByExecutionId(habitExecutionId);
    var name = res.IsSuccess() ? res.Get() : null;
    return string.IsNullOrWhiteSpace(name) ? "your habit" : name;
  }

  private string PlanPrice(string tier)
  {
    var plan = plans.GetPlan(tier);
    return plan.IsSuccess() ? FormatMoney(plan.Get().Price) : string.Empty;
  }

  private static string DisplayName(User? user)
  {
    var username = user?.Principal.Record.Username;
    return string.IsNullOrWhiteSpace(username) ? "there" : username;
  }

  private static string FormatMoney(Money m)
  {
    var amount = m.Amount.ToString("F2", CultureInfo.InvariantCulture);
    return m.Currency.Code == "USD" ? $"${amount}" : $"{amount} {m.Currency.Code}";
  }

  private static string FormatDate(DateTime utc)
  {
    return utc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
  }
}
