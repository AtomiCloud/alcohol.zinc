using App.Utility;
using FluentValidation;
using System.Globalization;

namespace App.Modules.Habit.API.V1;

public class CreateHabitReqValidator : AbstractValidator<CreateHabitReq>
{
    public CreateHabitReqValidator()
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.DayOfWeek)
            .NotEmpty()
            .Must(BeAValidDayOfWeek)
            .WithMessage("DayOfWeek must be a valid day name (e.g., Monday).");

        RuleFor(x => x.NotificationTime)
            .NotNull()
            .DateValid();

        RuleFor(x => x.Stake)
            .NotEmpty()
            .Must(BeAValidDecimal)
            .WithMessage("Stake must be a valid decimal number.")
            .Must(BeNonNegativeDecimal)
            .WithMessage("Stake must be non-negative.");

        RuleFor(x => x.Ratio)
            .NotEmpty()
            .Must(BeAValidDecimal)
            .WithMessage("Ratio must be a valid decimal number.")
            .Must(BeValidRatio)
            .WithMessage("Ratio must be between 0 and 100.");

        RuleFor(x => x.StartDate)
            .NotNull()
            .DateValid();

        RuleFor(x => x.EndDate)
           .NotNull()
            .DateValid()
            .Must((req, endDate) => BeEndDateAfterOrEqualStartDate(req.StartDate, endDate))
            .WithMessage("EndDate must be after or equal to StartDate.");
    }

    private static bool BeAValidDayOfWeek(string day)
    {
        return Enum.TryParse(typeof(DayOfWeek), day, true, out _);
    }

    private static bool BeAValidDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static bool BeNonNegativeDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0;
    }

    private static bool BeValidRatio(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0 && d <= 100;
    }

    private static bool BeEndDateAfterOrEqualStartDate(string start, string end)
    {
        var startDate = DateOnly.ParseExact(start, "dd-MM-yyyy", CultureInfo.InvariantCulture);
        var endDate = DateOnly.ParseExact(end, "dd-MM-yyyy", CultureInfo.InvariantCulture);
        return endDate >= startDate;
    }
}
