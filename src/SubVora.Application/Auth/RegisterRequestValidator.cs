using FluentValidation;

namespace SubVora.Application.Auth;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator(IBreachedPasswordChecker breachedPasswords)
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(AuthLimits.EmailMaxLength);
        RuleFor(r => r.Password).NewPassword(breachedPasswords);
    }
}
