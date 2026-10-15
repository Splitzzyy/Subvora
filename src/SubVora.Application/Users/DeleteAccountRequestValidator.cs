using FluentValidation;

namespace SubVora.Application.Users;

public class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty();
    }
}
