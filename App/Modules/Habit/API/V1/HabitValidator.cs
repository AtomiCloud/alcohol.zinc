using System.Globalization;
using App.Utility;
using FluentValidation;

namespace App.Modules.Habit.API.V1;

public class CreateHabitReqValidator : AbstractValidator<CreateHabitReq>
{
    public CreateHabitReqValidator()
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.DaysOfWeek)
            .NotEmpty()
            .Must(BeValidDaysOfWeek)
            .WithMessage("DaysOfWeek must contain valid day names (e.g., Monday, Tuesday).");

        RuleFor(x => x.NotificationTime)
            .NotNull()
            .TimeValid();

        RuleFor(x => x.Stake)
            .NotEmpty()
            .Must(BeAValidDecimal)
            .WithMessage("Stake must be a valid decimal number.")
            .Must(BeNonNegativeDecimal)
            .WithMessage("Stake must be non-negative.");


        RuleFor(x => x.CharityId)
            .NotEmpty();

        RuleFor(x => x.Timezone)
            .NotEmpty()
            .TimezoneValid();
    }

    private static bool BeValidDaysOfWeek(string[] days)
    {
        return days.All(day => Enum.TryParse(typeof(DayOfWeek), day, true, out _));
    }

    private static bool BeAValidDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static bool BeNonNegativeDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0;
    }


}

public class MarkDailyFailuresReqValidator : AbstractValidator<MarkDailyFailuresReq>
{
    public MarkDailyFailuresReqValidator()
    {
        RuleFor(x => x.Date)
            .NotEmpty()
            .DateValid();

        RuleFor(x => x.HabitIds)
            .NotEmpty()
            .WithMessage("UserIds list cannot be empty.");
    }
}

public class UpdateHabitReqValidator : AbstractValidator<UpdateHabitReq>
{
    public UpdateHabitReqValidator()
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.DaysOfWeek)
            .NotEmpty()
            .Must(BeValidDaysOfWeek)
            .WithMessage("DaysOfWeek must contain valid day names (e.g., Monday, Tuesday).");

        RuleFor(x => x.NotificationTime)
            .NotNull()
            .TimeValid();

        RuleFor(x => x.Stake)
            .NotEmpty()
            .Must(BeAValidDecimal)
            .WithMessage("Stake must be a valid decimal number.")
            .Must(BeNonNegativeDecimal)
            .WithMessage("Stake must be non-negative.");


        RuleFor(x => x.CharityId)
            .NotEmpty();

        RuleFor(x => x.Timezone)
            .NotEmpty()
            .TimezoneValid();
    }

    private static bool BeValidDaysOfWeek(string[] days)
    {
        return days.All(day => Enum.TryParse(typeof(DayOfWeek), day, false, out _));
    }

    private static bool BeAValidDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static bool BeNonNegativeDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0;
    }

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

    When(x => x.Skip != null, () => {
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
