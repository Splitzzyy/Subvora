using FluentValidation;

namespace SubVora.Application.Auth;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(AuthLimits.EmailMaxLength);
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8);
    }
}
