using FluentValidation;

namespace SubVora.Application.Auth;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(AuthLimits.EmailMaxLength);
        RuleFor(r => r.Password).NotEmpty();
    }
}
