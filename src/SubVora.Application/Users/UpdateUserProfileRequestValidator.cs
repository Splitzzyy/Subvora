using FluentValidation;
using SubVora.Application.Subscriptions;

namespace SubVora.Application.Users;

public class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(r => r.PreferredCurrency)
            .NotEmpty()
            .Length(3)
            .Must(CurrencyCodes.IsValid)
            .WithMessage("'{PropertyName}' must be a valid ISO-4217 currency code.");
        RuleFor(r => r.DefaultAlertDaysAdvance)
            .GreaterThan(0)
            .When(r => r.DefaultAlertDaysAdvance.HasValue);
    }
}
