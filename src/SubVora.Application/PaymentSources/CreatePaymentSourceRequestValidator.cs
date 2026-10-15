using FluentValidation;

namespace SubVora.Application.PaymentSources;

public class CreatePaymentSourceRequestValidator : AbstractValidator<CreatePaymentSourceRequest>
{
    public CreatePaymentSourceRequestValidator()
    {
        RuleFor(r => r.Id).NotEqual(Guid.Empty);
        RuleFor(r => r.Label).NotEmpty().MaximumLength(100);
        RuleFor(r => r.SourceType).IsInEnum();
    }
}
