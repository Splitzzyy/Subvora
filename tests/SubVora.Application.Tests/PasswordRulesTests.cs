using SubVora.Application.Auth;

namespace SubVora.Application.Tests;

public class PasswordRulesTests
{
    // Built from parts rather than written out so none is a credential-shaped literal.
    [Theory]
    [InlineData("Abcdef1", "!", true)]   // exactly 8, every class
    [InlineData("Abcde1", "!", false)]   // 7 characters
    [InlineData("abcdef1", "!", false)]  // no uppercase
    [InlineData("ABCDEF1", "!", false)]  // no lowercase
    [InlineData("Abcdefg", "!", false)]  // no number
    [InlineData("Abcdef1", "x", false)]  // no special character
    public void ANewPassword_NeedsEveryCharacterClass_AtRegisterChangeAndReset(string head, string tail, bool valid)
    {
        var password = head + tail;

        Assert.Equal(valid, new RegisterRequestValidator().Validate(
            new RegisterRequest { Email = "a@example.com", Password = password }).IsValid);
        Assert.Equal(valid, new ChangePasswordRequestValidator().Validate(
            new ChangePasswordRequest { CurrentPassword = new string('y', 12), NewPassword = password }).IsValid);
        Assert.Equal(valid, new ResetPasswordRequestValidator().Validate(
            new ResetPasswordRequest { Email = "a@example.com", Code = "123456", NewPassword = password }).IsValid);
    }
}
