using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SubVora.Application.Auth;
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
    private readonly IAuthService _authService;
    private readonly IValidator<DeleteAccountRequest> _deleteValidator;

    public UsersController(
        IUserRepository userRepository,
        IValidator<UpdateUserProfileRequest> updateValidator,
        IAuthService authService,
        IValidator<DeleteAccountRequest> deleteValidator)
    {
        _userRepository = userRepository;
        _updateValidator = updateValidator;
        _authService = authService;
        _deleteValidator = deleteValidator;
    }

    /// <summary>Gets the authenticated user's own profile.</summary>
    /// <remarks>
    /// 404 rather than a 200 carrying null when the row is gone - the same answer <c>PUT</c> gives
    /// on this resource. Only reachable if the row vanished between authenticating and reading it,
    /// but a 200 with an empty body is indistinguishable from a successful read, so a client has no
    /// way to tell the two apart.
    /// </remarks>
    /// <response code="200">Returns the caller's profile.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">The caller's user row no longer exists.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var profile = await _userRepository.GetProfileAsync(GetUserId(), cancellationToken);
        return profile is null ? NotFound() : Ok(profile);
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

    /// <summary>Deletes the authenticated user's account and all of its data.</summary>
    /// <remarks>
    /// Needs the current password as well as the access token, as change-password does: a stolen
    /// token alone must not be able to destroy the account. Subscriptions, categories, payment
    /// sources and every session go with it, and the email address is free to register again.
    /// <para>
    /// An access token already issued stays valid until it expires (at most 15 minutes), but every
    /// request it makes finds no account: reads answer 404, writes 401.
    /// </para>
    /// </remarks>
    /// <response code="204">The account was deleted.</response>
    /// <response code="400">The payload failed validation, or the current password is wrong.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpDelete("me")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _deleteValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        if (!await _authService.DeleteAccountAsync(GetUserId(), request.CurrentPassword, cancellationToken))
        {
            // 400, not 401, for the same reason as change-password: the caller is authenticated, it
            // is the password in the body that is wrong, and a 401 would send the app to refresh.
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = "Your current password is incorrect.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        return NoContent();
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
