using CSharp_Result;
using Domain;
using Domain.Allowance;
using Domain.Configuration;

namespace App.Modules.Allowance;

  public class AllowanceService(IConfigurationService configurationService) : IAllowanceService
  {
  public Task<Result<UserMonthWindow>> GetUserMonthWindow(string userId, DateTime? utcNow = null)
  {
    var now = utcNow ?? DateTime.UtcNow;
    return configurationService
      .GetByUserId(userId)
      .NullToError(userId)
      .Then(cfg => cfg.Principal.Record.Timezone, Errors.MapNone)
      .Then(tzId =>
      {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var userNow = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
        var monthStart = new DateOnly(userNow.Year, userNow.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return new UserMonthWindow(tzId, monthStart, monthEnd, userNow);
      }, Errors.MapNone);
  }

  public Task<Result<(string Timezone, DateOnly Today)>> GetUserToday(string userId, DateTime? utcNow = null)
  {
    var now = utcNow ?? DateTime.UtcNow;
    return configurationService
      .GetByUserId(userId)
      .NullToError(userId)
      .Then(cfg => cfg.Principal.Record.Timezone, Errors.MapNone)
      .Then(tzId =>
      {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var userNow = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
        return (tzId, DateOnly.FromDateTime(userNow));
      }, Errors.MapNone);
  }
}
