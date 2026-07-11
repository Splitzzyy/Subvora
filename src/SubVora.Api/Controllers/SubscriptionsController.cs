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

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

        return CreatedAtAction(nameof(Create), new { id = created.Id }, ToDto(created));
    }

    private static SubscriptionDto ToDto(UserSubscription subscription) => new()
    {
        Id = subscription.Id,
        CustomName = subscription.CustomName,
        CostAmount = subscription.CostAmount,
        Currency = subscription.Currency,
        CycleCadence = subscription.CycleCadence,
        PurchaseDate = subscription.PurchaseDate,
        NextBillingDate = subscription.NextBillingDate,
        AlertDaysAdvance = subscription.AlertDaysAdvance,
        CategoryId = subscription.CategoryId,
        PaymentSourceId = subscription.PaymentSourceId,
        IsFreeTrial = subscription.IsFreeTrial,
        IsActive = subscription.IsActive,
        CreatedAt = subscription.CreatedAt,
    };
}
