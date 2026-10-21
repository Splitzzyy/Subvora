using SubVora.Application.Auth;

namespace SubVora.Application.Tests;

public class PasswordRulesTests
{
    // Built from parts rather than written out so none is a credential-shaped literal.
    [Theory]
    [InlineData("Zqvwkm1", "!", true)]   // exactly 8, every class
    [InlineData("Passwo", "rd@1", true)] // a common word is fine once every class is there
    [InlineData("Zqvwk1", "!", false)]   // 7 characters
    [InlineData("zqvwkm1", "!", false)]  // no uppercase
    [InlineData("ZQVWKM1", "!", false)]  // no lowercase
    [InlineData("Zqvwkmn", "!", false)]  // no number
    [InlineData("Zqvwkm1", "x", false)]  // no special character
    [InlineData("Zqvwkm1", " ", false)]  // whitespace is not a special character
    public void ANewPassword_NeedsEveryCharacterClass_AtRegisterChangeAndReset(string head, string tail, bool valid)
    {
        var result = Validate(head + tail);

        Assert.All(result, r => Assert.Equal(valid, r.IsValid));
        if (!valid)
        {
            Assert.All(result, r => Assert.Contains(r.Errors, e => e.ErrorMessage == PasswordRules.RequirementsMessage));
        }
    }

    private static FluentValidation.Results.ValidationResult[] Validate(string password) =>
    [
        new RegisterRequestValidator().Validate(new RegisterRequest { Email = "a@example.com", Password = password }),
        new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest { CurrentPassword = new string('y', 12), NewPassword = password }),
        new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest { Email = "a@example.com", Code = "123456", NewPassword = password }),
    ];
}
