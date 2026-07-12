using FluentValidation;

namespace SubVora.Application.Auth;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$").WithMessage("Code must be 6 digits.");
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8);
    }
}
