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
