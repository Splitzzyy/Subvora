using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Devices;

namespace SubVora.Api.Controllers;

/// <summary>Push-notification device token registration for the authenticated user.</summary>
[Authorize]
[ApiController]
[Route("api/v1/devices")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceTokenRepository _deviceTokenRepository;
    private readonly IValidator<RegisterDeviceTokenRequest> _registerValidator;

    public DevicesController(IDeviceTokenRepository deviceTokenRepository, IValidator<RegisterDeviceTokenRequest> registerValidator)
    {
        _deviceTokenRepository = deviceTokenRepository;
        _registerValidator = registerValidator;
    }

    /// <summary>Registers (or refreshes) a push-notification device token for the authenticated user.</summary>
    /// <response code="200">The token was registered or its last-seen timestamp was refreshed.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var deviceToken = await _deviceTokenRepository.UpsertAsync(GetUserId(), request, cancellationToken);
        return Ok(deviceToken);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
