using FluentValidation;

namespace SubVora.Application.Matching;

public class ResolveSubscriptionRequestValidator : AbstractValidator<ResolveSubscriptionRequest>
{
    public ResolveSubscriptionRequestValidator()
    {
        RuleFor(r => r.Input).NotEmpty().MaximumLength(200);
    }
}
