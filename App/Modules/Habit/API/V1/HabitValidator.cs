using System.Globalization;
using App.StartUp.Options;
using App.Utility;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace App.Modules.Habit.API.V1;

public class CreateHabitReqValidator : AbstractValidator<CreateHabitReq>
{
    public CreateHabitReqValidator(IOptionsMonitor<HabitOption> options)
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.DaysOfWeek)
            .NotEmpty()
            .DaysOfWeekValid();

        RuleFor(x => x.NotificationTime)
            .NotNull()
            .TimeValid();

        RuleFor(x => x.Stake)
            .StakeValid(options);

        RuleFor(x => x.CharityId)
            .Must(id => id is { } g && g != Guid.Empty)
            .When(x => HabitReqRules.HasMoneyStake(x.Stake))
            .WithMessage("A habit with a stake needs a charity.");

        RuleFor(x => x.Timezone)
            .NotEmpty()
            .TimezoneValid();

        RuleFor(x => x.DeadlineTime!)
            .TimeValid()
            .When(x => x.DeadlineTime != null);
    }
}

public class UpdateHabitReqValidator : AbstractValidator<UpdateHabitReq>
{
    public UpdateHabitReqValidator(IOptionsMonitor<HabitOption> options)
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.DaysOfWeek)
            .NotEmpty()
            .DaysOfWeekValid();

        RuleFor(x => x.NotificationTime)
            .NotNull()
            .TimeValid();

        RuleFor(x => x.Stake)
            .StakeValid(options);

        RuleFor(x => x.CharityId)
            .Must(id => id is { } g && g != Guid.Empty)
            .When(x => HabitReqRules.HasMoneyStake(x.Stake))
            .WithMessage("A habit with a stake needs a charity.");

        RuleFor(x => x.Timezone)
            .NotEmpty()
            .TimezoneValid();

        RuleFor(x => x.DeadlineTime!)
            .TimeValid()
            .When(x => x.DeadlineTime != null);
    }
}

// Rules shared by the create and update requests.
public static class HabitReqRules
{
    // A weekday name in any case ("Friday", "friday"). Numbers ("1", "999") and lists
    // ("Monday,Tuesday") are not names, although Enum.TryParse accepts them.
    public static bool TryParseDay(string? day, out DayOfWeek value)
    {
        value = default;
        return !string.IsNullOrEmpty(day) && day.All(char.IsLetter) && Enum.TryParse(day, true, out value);
    }

    // The stored form of a valid day name, e.g. "friday" -> "Friday".
    public static string CanonicalDay(string day) => TryParseDay(day, out var d) ? d.ToString() : day;

    public static IRuleBuilderOptions<T, string[]> DaysOfWeekValid<T>(this IRuleBuilder<T, string[]> rule) =>
        rule.Must(days => days.All(d => TryParseDay(d, out _)))
            .WithMessage("DaysOfWeek must contain valid day names (e.g., Monday, Tuesday).");

    // Stakes are whole cents up to the configured maximum (stored as int cents).
    public static IRuleBuilderOptions<T, string> StakeValid<T>(this IRuleBuilder<T, string> rule,
        IOptionsMonitor<HabitOption> options) =>
        rule.NotEmpty()
            .Must(s => TryParseStake(s, out _))
            .WithMessage("Stake must be a valid decimal number.")
            .Must(s => !TryParseStake(s, out var d) || d >= 0)
            .WithMessage("Stake must be non-negative.")
            .Must(s => !TryParseStake(s, out var d) || decimal.Round(d, 2) == d)
            .WithMessage("Stake can have at most 2 decimal places.")
            .Must(s => !TryParseStake(s, out var d) || d <= options.CurrentValue.MaxStakeCents / 100m)
            .WithMessage(_ => $"Stake must be at most {options.CurrentValue.MaxStakeCents / 100m:F2}.");

    // An unparsable stake fails the Stake rules; it isn't also reported as a missing charity.
    public static bool HasMoneyStake(string? stake) => TryParseStake(stake, out var d) && d > 0;

    private static bool TryParseStake(string? s, out decimal d) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d);
}

public class SearchHabitQueryValidator : AbstractValidator<SearchHabitQuery>
{
  public SearchHabitQueryValidator()
  {
    When(x => x.Task != null, () => {
      RuleFor(x => x.Task)
        .MaximumLength(256)
        .WithMessage("Task must be 256 characters or less.");
    });

    When(x => x.UserId != null, () => {
      RuleFor(x => x.UserId)
        .MaximumLength(128)
        .WithMessage("UserId must be 128 characters or less.");
    });

    When(x => x.Limit != null, () => {
      RuleFor(x => x.Limit)
        .Limit();
    });

    When(x => x.Skip != null, () =>
    {
      RuleFor(x => x.Skip)
        .Skip();
    });
  }
}

public class SearchHabitExecutionQueryValidator: AbstractValidator<SearchHabitExecutionQuery>
{
  public SearchHabitExecutionQueryValidator()
  {
    When(x => x.Date != null, () => {
      RuleFor(x => x.Date)
        .NullableDateValid();
    });

    When(x => x.Limit != null, () => {
      RuleFor(x => x.Limit)
        .Limit();
    });

    When(x => x.Skip != null, () => {
      RuleFor(x => x.Skip)
        .Skip();
    });
  }
}

public class SearchOverviewQueryValidator : AbstractValidator<OverviewQuery>
{
  public SearchOverviewQueryValidator()
  {
    When(x => x.Limit != null, () =>
    {
      RuleFor(x => x.Limit).Limit();
    });

    When(x => x.Skip != null, () =>
    {
      RuleFor(x => x.Skip).Skip();
    });
  }
}

public class ReorderHabitsReqValidator : AbstractValidator<ReorderHabitsReq>
{
  public ReorderHabitsReqValidator()
  {
    RuleFor(x => x.HabitIds)
      .NotNull();

    RuleForEach(x => x.HabitIds)
      .Must(id => Guid.TryParse(id, out _))
      .WithMessage("Each habit id must be a GUID.");
  }
}
