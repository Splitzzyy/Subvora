using SubVora.Application.Auth;

namespace SubVora.Application.Tests;

public class BreachedPasswordValidatorTests
{
    /// <summary>Built rather than written out so it is not a credential-shaped literal.</summary>
    private static readonly string Password = new('x', 12);

    private static readonly IBreachedPasswordChecker Breached = new StubChecker(true);

    [Fact]
    public async Task ABreachedPassword_IsRejected_AtRegisterChangeAndReset()
    {
        AssertBreached(await new RegisterRequestValidator(Breached).ValidateAsync(
            new RegisterRequest { Email = "a@example.com", Password = Password }));
        AssertBreached(await new ChangePasswordRequestValidator(Breached).ValidateAsync(
            new ChangePasswordRequest { CurrentPassword = new string('y', 12), NewPassword = Password }));
        AssertBreached(await new ResetPasswordRequestValidator(Breached).ValidateAsync(
            new ResetPasswordRequest { Email = "a@example.com", Code = "123456", NewPassword = Password }));
    }

    [Fact]
    public async Task AShortPassword_NeverReachesTheCorpus()
    {
        var checker = new StubChecker(true);

        var result = await new RegisterRequestValidator(checker).ValidateAsync(new RegisterRequest { Email = "a@example.com", Password = new string('x', 5) });

        Assert.False(result.IsValid);
        Assert.Equal(0, checker.Calls);
    }

    [Fact]
    public async Task ACleanPassword_Passes()
    {
        var result = await new RegisterRequestValidator(NeverBreached.Instance).ValidateAsync(
            new RegisterRequest { Email = "a@example.com", Password = Password });

        Assert.True(result.IsValid);
    }

    private static void AssertBreached(FluentValidation.Results.ValidationResult result) =>
        Assert.Contains(result.Errors, e => e.ErrorMessage == PasswordRules.BreachedMessage);

    private sealed class StubChecker(bool breached) : IBreachedPasswordChecker
    {
        public int Calls { get; private set; }

        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(breached);
        }
    }
}

public sealed class NeverBreached : IBreachedPasswordChecker
{
    public static readonly NeverBreached Instance = new();

    public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
