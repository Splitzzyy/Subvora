using FluentValidation;

namespace SubVora.Application.Subscriptions;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    /// <summary>
    /// cost_amount is numeric(12,2), so 9,999,999,999.99 is the largest value the column holds.
    /// .NET's decimal holds far more, so without this bound an oversized cost passed validation and
    /// failed in Postgres with SQLSTATE 22003 - a DbUpdateException no controller catches, which
    /// GlobalExceptionHandler turned into a 500. A client sending a bad number should get a 400
    /// naming the field.
    /// </summary>
    private const decimal MaxCostAmount = 10_000_000_000m;

    /// <summary>
    /// No column limit behind this one - alert_days_advance is a plain int. It bounds the client:
    /// RenewalNotificationPlanner computes NextBillingDate.AddDays(-AlertDaysAdvance), which throws
    /// for values near int.MaxValue. LocalRenewalNotificationScheduler catches and logs, so the app
    /// degrades to scheduling no reminders at all rather than crashing - a silent loss of the
    /// feature. A year of lead time is past any real use.
    /// </summary>
    private const int MaxAlertDaysAdvance = 365;

    public CreateSubscriptionRequestValidator()
    {
        RuleFor(r => r.CustomName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.CostAmount)
            .GreaterThan(0)
            .LessThan(MaxCostAmount)
            .WithMessage($"'{{PropertyName}}' must be less than {MaxCostAmount:N0}.");
        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .Must(CurrencyCodes.IsValid)
            .WithMessage("'{PropertyName}' must be a valid ISO-4217 currency code.");
        RuleFor(r => r.CycleCadence).IsInEnum();
        RuleFor(r => r.AlertDaysAdvance)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxAlertDaysAdvance)
            .When(r => r.AlertDaysAdvance.HasValue);
        RuleFor(r => r.NextBillingDate)
            .GreaterThanOrEqualTo(r => r.PurchaseDate)
            .WithMessage("'{PropertyName}' must be on or after PurchaseDate.");
    }
}
