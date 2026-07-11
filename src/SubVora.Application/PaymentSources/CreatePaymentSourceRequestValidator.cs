using FluentValidation;

namespace SubVora.Application.PaymentSources;

public class CreatePaymentSourceRequestValidator : AbstractValidator<CreatePaymentSourceRequest>
{
    public CreatePaymentSourceRequestValidator()
    {
        RuleFor(r => r.Label).NotEmpty().MaximumLength(100);
        RuleFor(r => r.SourceType).IsInEnum();
    }
}
