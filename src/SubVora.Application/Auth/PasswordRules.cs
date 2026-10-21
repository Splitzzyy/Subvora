using System.Text.RegularExpressions;
using FluentValidation;

namespace SubVora.Application.Auth;

public static class PasswordRules
{
    /// <summary>One message for every requirement, so a refused password says everything it is missing at once.</summary>
    public const string RequirementsMessage =
        "Password must be at least 8 characters and include an uppercase letter, a lowercase letter, a number and a special character.";

    public const string CommonMessage =
        "This password is too common, so attackers try it first. Choose a different one.";

    /// <summary>
    /// Checked locally, so nothing derived from a password leaves the server. Case-insensitive, and
    /// also matched with the digits and symbols around the word stripped: the character rule pushes
    /// people to "Password1!", which is exactly the shape credential stuffing tries first.
    /// </summary>
    private static readonly HashSet<string> CommonPasswords = LoadCommonPasswords();

    /// <summary>The one rule every password a user chooses goes through: register, change and reset.</summary>
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MeetsRequirements)
            .WithMessage(RequirementsMessage)
            .Must(password => !IsCommon(password))
            .WithMessage(CommonMessage);

    private static bool MeetsRequirements(string password) =>
        password.Length >= 8
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        // Punctuation or symbol, not merely "not a letter or digit": a space would otherwise count,
        // and a stray trailing one is easy to type once and impossible to sign in without.
        && password.Any(c => char.IsPunctuation(c) || char.IsSymbol(c));

    private static bool IsCommon(string password) =>
        CommonPasswords.Contains(password)
        || CommonPasswords.Contains(Regex.Replace(password, @"^\P{L}+|\P{L}+$", ""));

    private static HashSet<string> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordRules).Assembly.GetManifestResourceStream("SubVora.Application.Auth.common-passwords.txt")
            ?? throw new InvalidOperationException("The common-passwords list is not embedded in SubVora.Application.");
        using var reader = new StreamReader(stream);
        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith('#'))
            {
                passwords.Add(line.Trim());
            }
        }

        return passwords;
    }
}
