using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubVora.Application;
using SubVora.Application.PaymentSources;
using SubVora.Infrastructure.Data;

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
    /// payment source already made (200) instead of a duplicate, as long as the details match.
    /// Without it the server picks the id.
    /// </remarks>
    /// <response code="201">The payment source was created.</response>
    /// <response code="200">A replay of a create that already succeeded with the same id and details.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="409">The id is already in use, by another record or by this create with different details.</response>
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

        // A replay of a create that already went through is not a new row, so it is let through to
        // the insert, which hands it back.
        // ponytail: count-then-insert can overshoot by the number of concurrent creates; a hard
        // DB-side cap needs a trigger, not worth it for an abuse guard.
        if (await _paymentSourceRepository.CountForUserAsync(userId, cancellationToken) >= UserQuotas.PaymentSources
            && !(request.Id is Guid replayId && await _paymentSourceRepository.GetByIdAsync(replayId, userId, cancellationToken) is not null))
        {
            ModelState.AddModelError(string.Empty, $"You can have at most {UserQuotas.PaymentSources} payment sources.");
            return ValidationProblem(ModelState);
        }

        PaymentSourceDto paymentSource;
        try
        {
            paymentSource = await _paymentSourceRepository.AddAsync(userId, request, cancellationToken);
        }
        catch (DbUpdateException ex) when (request.Id is Guid takenId && ex.IsUniqueViolation())
        {
            // The id is taken. A retry of a create that went through with the same details gets that
            // record back. Different details (the form was changed after the lost response) or
            // someone else's id, deliberately indistinguishable, are a conflict: handing back the old
            // record as if it were this one would silently drop the change.
            var existing = await _paymentSourceRepository.GetByIdAsync(takenId, userId, cancellationToken);
            return existing is not null && existing.Label == request.Label && existing.SourceType == request.SourceType
                ? Ok(existing)
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
