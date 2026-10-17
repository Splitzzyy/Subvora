using SubVora.Application.Auth;

namespace SubVora.Application.Tests;

public class EmailLengthValidatorTests
{
    /// <summary>Only its length matters here; built rather than written out so it is not a credential-shaped literal.</summary>
    private static readonly string Password = new('x', 12);

    private static string EmailOfLength(int length) => new string('a', length - "@example.com".Length) + "@example.com";

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public async Task EveryEmailValidator_StopsAtTheColumnLength(int length, bool valid)
    {
        var email = EmailOfLength(length);

        Assert.Equal(valid, (await new RegisterRequestValidator(NeverBreached.Instance).ValidateAsync(new RegisterRequest { Email = email, Password = Password })).IsValid);
        Assert.Equal(valid, new LoginRequestValidator().Validate(new LoginRequest { Email = email, Password = Password }).IsValid);
        Assert.Equal(valid, new ForgotPasswordRequestValidator().Validate(new ForgotPasswordRequest { Email = email }).IsValid);
        Assert.Equal(valid, (await new ResetPasswordRequestValidator(NeverBreached.Instance).ValidateAsync(new ResetPasswordRequest { Email = email, Code = "123456", NewPassword = Password })).IsValid);
    }
}
