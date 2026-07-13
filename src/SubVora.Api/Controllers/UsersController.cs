using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Users;

namespace SubVora.Api.Controllers;

/// <summary>The authenticated user's own profile.</summary>
[Authorize]
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IValidator<UpdateUserProfileRequest> _updateValidator;

    public UsersController(IUserRepository userRepository, IValidator<UpdateUserProfileRequest> updateValidator)
    {
        _userRepository = userRepository;
        _updateValidator = updateValidator;
    }

    /// <summary>Gets the authenticated user's own profile.</summary>
    /// <response code="200">Returns the caller's profile.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var profile = await _userRepository.GetProfileAsync(GetUserId(), cancellationToken);
        return Ok(profile);
    }

    /// <summary>Updates the authenticated user's own profile.</summary>
    /// <response code="200">Returns the updated profile.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var updated = await _userRepository.UpdateProfileAsync(GetUserId(), request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
