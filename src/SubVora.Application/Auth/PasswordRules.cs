using FluentValidation;

namespace SubVora.Application.Auth;

public static class PasswordRules
{
    /// <summary>One message for every requirement, so a refused password says everything it is missing at once.</summary>
    public const string RequirementsMessage =
        "Password must be at least 8 characters and include an uppercase letter, a lowercase letter, a number and a special character.";

    /// <summary>The one rule every password a user chooses goes through: register, change and reset.</summary>
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MeetsRequirements)
            .WithMessage(RequirementsMessage);

    private static bool MeetsRequirements(string password) =>
        password.Length >= 8
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        // Punctuation or symbol, not merely "not a letter or digit": a space would otherwise count,
        // and a stray trailing one is easy to type once and impossible to sign in without.
        && password.Any(c => char.IsPunctuation(c) || char.IsSymbol(c));
}
