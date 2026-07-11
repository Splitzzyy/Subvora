using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// <response code="201">The payment source was created.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentSourceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentSourceRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var paymentSource = await _paymentSourceRepository.AddAsync(GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetAll), paymentSource);
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
