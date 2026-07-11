using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Entities;

namespace SubVora.Api.Controllers;

/// <summary>Managing a user's tracked subscriptions.</summary>
[Authorize]
[ApiController]
[Route("api/v1/subscriptions")]
[Produces("application/json")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IValidator<CreateSubscriptionRequest> _createValidator;

    public SubscriptionsController(ISubscriptionRepository subscriptionRepository, IValidator<CreateSubscriptionRequest> createValidator)
    {
        _subscriptionRepository = subscriptionRepository;
        _createValidator = createValidator;
    }

    /// <summary>Lists the authenticated user's subscriptions.</summary>
    /// <response code="200">Returns the caller's subscriptions.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var subscriptions = await _subscriptionRepository.GetAllForUserAsync(GetUserId(), cancellationToken);
        return Ok(subscriptions);
    }

    /// <summary>Gets a single subscription owned by the authenticated user.</summary>
    /// <remarks>Returns 404 (not 403) when the subscription exists but belongs to another user, to avoid revealing its existence.</remarks>
    /// <response code="200">Returns the subscription.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such subscription owned by the caller.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(id, GetUserId(), cancellationToken);
        return subscription is null ? NotFound() : Ok(subscription);
    }

    /// <summary>Creates a subscription for the authenticated user.</summary>
    /// <response code="201">The subscription was created.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var userId = GetUserId();

        var subscription = new UserSubscription
        {
            UserId = userId,
            CustomName = request.CustomName,
            CostAmount = request.CostAmount,
            Currency = request.Currency.ToUpperInvariant(),
            CycleCadence = request.CycleCadence,
            PurchaseDate = request.PurchaseDate,
            NextBillingDate = request.NextBillingDate,
            AlertDaysAdvance = request.AlertDaysAdvance,
            CategoryId = request.CategoryId,
            PaymentSourceId = request.PaymentSourceId,
            IsFreeTrial = request.IsFreeTrial,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var created = await _subscriptionRepository.AddAsync(subscription, cancellationToken);

        // Re-fetch through the joined DTO query so the create response has the same shape
        // (resolved category name/payment source label/catalog logo) as GetById/GetAll.
        var dto = await _subscriptionRepository.GetByIdAsync(created.Id, userId, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, dto);
    }

    /// <summary>Updates a subscription owned by the authenticated user.</summary>
    /// <remarks>Uses the same request shape and validation rules as create - the editable field set is identical.</remarks>
    /// <response code="200">Returns the updated subscription.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such subscription owned by the caller.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var updated = await _subscriptionRepository.UpdateAsync(id, GetUserId(), request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
