using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Auth;

namespace SubVora.Api.Controllers;

/// <summary>Account registration and session (access/refresh token) management.</summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    /// <summary>Creates a new account.</summary>
    /// <response code="201">Account created.</response>
    /// <response code="400">The email or password failed validation.</response>
    /// <response code="409">An account with this email already exists.</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisteredUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _authService.RegisterAsync(request, cancellationToken);
        if (result.EmailAlreadyExists)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        return CreatedAtAction(nameof(Register), new { id = result.User!.Id }, result.User);
    }

    /// <summary>Signs in with email and password.</summary>
    /// <response code="200">Returns a new access/refresh token pair.</response>
    /// <response code="400">The email or password failed validation.</response>
    /// <response code="401">Invalid email or password.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _loginValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _authService.LoginAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(result.Tokens);
    }

    /// <summary>Exchanges a valid refresh token for a new access/refresh pair, rotating it.</summary>
    /// <remarks>
    /// Reuse of an already-rotated (stale) refresh token revokes every active refresh token
    /// for that user, since it signals the token may have been stolen.
    /// </remarks>
    /// <response code="200">Returns a new access/refresh token pair.</response>
    /// <response code="401">The refresh token is invalid, expired, or was already rotated.</response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, cancellationToken);
        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        return Ok(result.Tokens);
    }

    /// <summary>Revokes the caller's refresh token, ending that session.</summary>
    /// <remarks>Requires a valid access token. Always returns 204, even if the presented refresh token is missing, already revoked, or does not belong to the caller - logout never reveals whether a given token exists.</remarks>
    /// <response code="204">The token was revoked (or the request was otherwise a no-op).</response>
    /// <response code="401">The caller is not authenticated.</response>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.LogoutAsync(userId, request.RefreshToken, cancellationToken);
        return NoContent();
    }
}
