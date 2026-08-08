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
        // Same ceiling as CreateSubscriptionRequestValidator - this value becomes a subscription's
        // AlertDaysAdvance when one is created without an explicit lead time, so an unbounded
        // default would simply move the problem one step back.
        RuleFor(r => r.DefaultAlertDaysAdvance)
            .GreaterThan(0)
            .LessThanOrEqualTo(365)
            .When(r => r.DefaultAlertDaysAdvance.HasValue);
    }
}
