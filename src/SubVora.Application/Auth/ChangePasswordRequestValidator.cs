using FluentValidation;

namespace SubVora.Application.Auth;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty();

        // Same floor as registration - a password changed to something weaker than one the app
        // would have refused at sign-up is a gap, not a convenience.
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8);

        RuleFor(r => r.NewPassword)
            .NotEqual(r => r.CurrentPassword)
            .WithMessage("'New Password' must be different from your current password.");
    }
}
