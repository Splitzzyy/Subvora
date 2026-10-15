using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SubVora.Application.PaymentSources;

namespace SubVora.Api.Controllers;

/// <summary>A user's own payment methods, attachable to subscriptions.</summary>
[Authorize]
[ApiController]
[Route("api/v1/payment-sources")]
[Produces("application/json")]
public class PaymentSourcesController : ControllerBase
{
    private readonly IPaymentSourceRepository _paymentSourceRepository;
    private readonly IValidator<CreatePaymentSourceRequest> _createValidator;

    public PaymentSourcesController(IPaymentSourceRepository paymentSourceRepository, IValidator<CreatePaymentSourceRequest> createValidator)
    {
        _paymentSourceRepository = paymentSourceRepository;
        _createValidator = createValidator;
    }

    /// <summary>Lists the authenticated user's own payment sources.</summary>
    /// <response code="200">Returns the caller's payment sources.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentSourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var paymentSources = await _paymentSourceRepository.GetForUserAsync(GetUserId(), cancellationToken);
        return Ok(paymentSources);
    }

    /// <summary>Creates a payment source owned by the authenticated user.</summary>
    /// <remarks>
    /// Send an <c>Id</c> chosen once per create form and a retry of the same create returns the
    /// payment source already made (200) instead of a duplicate. Without it the server picks the id.
    /// </remarks>
    /// <response code="201">The payment source was created.</response>
    /// <response code="200">A replay of a create that already succeeded with the same id.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="409">The id is already in use by another record.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentSourceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(PaymentSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentSourceRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var userId = GetUserId();

        // A replay of a create that already went through: hand back what was made, write nothing.
        if (request.Id is Guid requestedId
            && await _paymentSourceRepository.GetByIdAsync(requestedId, userId, cancellationToken) is { } existing)
        {
            return Ok(existing);
        }

        PaymentSourceDto paymentSource;
        try
        {
            paymentSource = await _paymentSourceRepository.AddAsync(userId, request, cancellationToken);
        }
        catch (DbUpdateException ex) when (request.Id is Guid racedId
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent replay inserted the same id between our check and our insert. Theirs
            // is the record; if the id belongs to someone else, it is simply taken.
            return await _paymentSourceRepository.GetByIdAsync(racedId, userId, cancellationToken) is { } raced
                ? Ok(raced)
                : Conflict(new { message = "That id is already in use." });
        }

        return CreatedAtAction(nameof(GetAll), paymentSource);
    }

    /// <summary>Updates a payment source owned by the authenticated user.</summary>
    /// <remarks>In place, deliberately: the subscriptions foreign key is ON DELETE SET NULL, so deleting and recreating would quietly detach every subscription that used it.</remarks>
    /// <response code="200">Returns the updated payment source.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such payment source owned by the caller.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PaymentSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreatePaymentSourceRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var updated = await _paymentSourceRepository.UpdateAsync(id, GetUserId(), request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Deletes a payment source owned by the authenticated user.</summary>
    /// <response code="204">The payment source was deleted.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such payment source owned by the caller.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _paymentSourceRepository.DeleteAsync(id, GetUserId(), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
