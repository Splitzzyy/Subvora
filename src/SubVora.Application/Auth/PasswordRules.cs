using FluentValidation;

namespace SubVora.Application.Auth;

public static class PasswordRules
{
    public const string BreachedMessage =
        "This password has appeared in a data breach, so attackers try it first. Choose a different one.";

    /// <summary>
    /// The one rule every password a user chooses goes through: register, change and reset. The
    /// breach lookup runs last, and only once the cheap rules pass, so a blank or short password
    /// never costs a call to the corpus.
    /// </summary>
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilderInitial<T, string> rule, IBreachedPasswordChecker breachedPasswords) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(8)
            .MustAsync(async (password, cancellationToken) => !await breachedPasswords.IsBreachedAsync(password, cancellationToken))
            .WithMessage(BreachedMessage);
}
