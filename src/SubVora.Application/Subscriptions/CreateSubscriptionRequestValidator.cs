using FluentValidation;

namespace SubVora.Application.Subscriptions;

public class CreateSubscriptionRequestValidator : AbstractValidator<CreateSubscriptionRequest>
{
    public CreateSubscriptionRequestValidator()
    {
        RuleFor(r => r.CustomName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.CostAmount).GreaterThan(0);
        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .Must(CurrencyCodes.IsValid)
            .WithMessage("'{PropertyName}' must be a valid ISO-4217 currency code.");
        RuleFor(r => r.CycleCadence).IsInEnum();
        RuleFor(r => r.AlertDaysAdvance).GreaterThan(0);
        RuleFor(r => r.NextBillingDate)
            .GreaterThanOrEqualTo(r => r.PurchaseDate)
            .WithMessage("'{PropertyName}' must be on or after PurchaseDate.");
    }
}
