using FluentValidation;

namespace SubVora.Application.Devices;

public class RegisterDeviceTokenRequestValidator : AbstractValidator<RegisterDeviceTokenRequest>
{
    private static readonly string[] AllowedPlatforms = ["Android", "iOS"];

    public RegisterDeviceTokenRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty();
        RuleFor(r => r.Platform).NotEmpty().Must(p => AllowedPlatforms.Contains(p))
            .WithMessage("Platform must be 'Android' or 'iOS'.");
    }
}
