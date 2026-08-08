using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SubVora.Application.Categories;
using SubVora.Application.Matching;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Application.Users;
using SubVora.Domain.Entities;

namespace SubVora.Api.Controllers;

/// <summary>Managing a user's tracked subscriptions.</summary>
[Authorize]
[ApiController]
[Route("api/v1/subscriptions")]
[Produces("application/json")]
public class SubscriptionsController : ControllerBase
{
    private const int DefaultAlertDaysAdvance = 3;

    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IValidator<CreateSubscriptionRequest> _createValidator;
    private readonly ISubscriptionMatchService _subscriptionMatchService;
    private readonly IValidator<ResolveSubscriptionRequest> _resolveValidator;
    private readonly ISubscriptionCatalogSearchRepository _catalogSearchRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IPaymentSourceRepository _paymentSourceRepository;
    private readonly IUserRepository _userRepository;

    public SubscriptionsController(
        ISubscriptionRepository subscriptionRepository,
        IValidator<CreateSubscriptionRequest> createValidator,
        ISubscriptionMatchService subscriptionMatchService,
        IValidator<ResolveSubscriptionRequest> resolveValidator,
        ISubscriptionCatalogSearchRepository catalogSearchRepository,
        ICategoryRepository categoryRepository,
        IPaymentSourceRepository paymentSourceRepository,
        IUserRepository userRepository)
    {
        _subscriptionRepository = subscriptionRepository;
        _createValidator = createValidator;
        _subscriptionMatchService = subscriptionMatchService;
        _resolveValidator = resolveValidator;
        _catalogSearchRepository = catalogSearchRepository;
        _categoryRepository = categoryRepository;
        _paymentSourceRepository = paymentSourceRepository;
        _userRepository = userRepository;
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

        if (await ValidateReferencesAsync(request, userId, cancellationToken) is { } referenceProblem)
        {
            return referenceProblem;
        }

        var subscription = new UserSubscription
        {
            UserId = userId,
            CustomName = request.CustomName,
            CostAmount = request.CostAmount,
            Currency = request.Currency.ToUpperInvariant(),
            CycleCadence = request.CycleCadence,
            PurchaseDate = request.PurchaseDate,
            NextBillingDate = request.NextBillingDate,
            // Request value, then the user's global default, then the floor - same shape as
            // DashboardController resolving GetPreferredCurrencyAsync(...) ?? "USD".
            AlertDaysAdvance = request.AlertDaysAdvance
                ?? await _userRepository.GetDefaultAlertDaysAdvanceAsync(userId, cancellationToken)
                ?? DefaultAlertDaysAdvance,
            CategoryId = request.CategoryId,
            PaymentSourceId = request.PaymentSourceId,
            CatalogId = request.CatalogId,
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

        var userId = GetUserId();

        if (await ValidateReferencesAsync(request, userId, cancellationToken) is { } referenceProblem)
        {
            return referenceProblem;
        }

        var updated = await _subscriptionRepository.UpdateAsync(id, userId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Deletes a subscription owned by the authenticated user.</summary>
    /// <response code="204">The subscription was deleted.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such subscription owned by the caller.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _subscriptionRepository.DeleteAsync(id, GetUserId(), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Records the outstanding charge as paid and moves the subscription on one billing cycle.</summary>
    /// <remarks>
    /// The date settled is the subscription's current next_billing_date, not today: paying a 23 Apr
    /// charge on 7 Aug still settles 23 Apr, and the following charge falls a cycle after that date.
    /// A OneTime subscription has nothing further to bill, so it is deactivated instead of advanced.
    /// Nothing else moves next_billing_date - a date in the past means the charge is genuinely
    /// outstanding, which is what lets a client show it as overdue.
    /// </remarks>
    /// <response code="200">Returns the updated subscription.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such subscription owned by the caller.</response>
    [HttpPost("{id:guid}/mark-paid")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _subscriptionRepository.MarkPaidAsync(id, GetUserId(), cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Resolves free-text subscription input (e.g. "nflx mobile plan") to a catalog match via AI embedding + cosine similarity.</summary>
    /// <remarks>
    /// Similarity ≥0.85 auto-fills from the matched catalog entry; 0.70-0.85 returns the same match
    /// flagged for user confirmation; below 0.70 (or an empty catalog) returns no match and records
    /// the input as a new subscription_catalog entry for future matching.
    /// </remarks>
    /// <response code="200">Returns the resolution result.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="429">The caller exceeded the per-user rate limit for this endpoint.</response>
    [HttpPost("resolve")]
    [EnableRateLimiting("ai-resolve")]
    [ProducesResponseType(typeof(ResolveSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Resolve([FromBody] ResolveSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _resolveValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _subscriptionMatchService.ResolveAsync(request.Input, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns a 400 ValidationProblem when the request points at a row the caller may not use, or
    /// null when there's nothing to complain about. Lives here rather than in
    /// CreateSubscriptionRequestValidator because that validator is pure and has no repository
    /// access - the surrounding controllers already do cross-entity checks this way.
    /// <para>
    /// Ownership, not just existence. The foreign keys on user_subscriptions accept any id that is
    /// present in the target table, so without these checks a caller could attach another user's
    /// private category or payment source to their own subscription - and read the name and label
    /// back out through the joined DTO.
    /// </para>
    /// </summary>
    private async Task<IActionResult?> ValidateReferencesAsync(CreateSubscriptionRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        // The catalog is global and unowned, so existence is the whole check here.
        if (request.CatalogId is Guid catalogId && !await _catalogSearchRepository.ExistsAsync(catalogId, cancellationToken))
        {
            errors[nameof(CreateSubscriptionRequest.CatalogId)] = ["No subscription catalog entry exists with that id."];
        }

        // Deliberately the same message whether the row is missing or belongs to someone else -
        // telling the two apart would confirm that a given id exists.
        if (request.CategoryId is Guid categoryId && !await _categoryRepository.IsAccessibleToUserAsync(categoryId, userId, cancellationToken))
        {
            errors[nameof(CreateSubscriptionRequest.CategoryId)] = ["No category is available to you with that id."];
        }

        if (request.PaymentSourceId is Guid paymentSourceId && !await _paymentSourceRepository.IsOwnedByUserAsync(paymentSourceId, userId, cancellationToken))
        {
            errors[nameof(CreateSubscriptionRequest.PaymentSourceId)] = ["No payment source is available to you with that id."];
        }

        return errors.Count == 0 ? null : ValidationProblem(new ValidationProblemDetails(errors));
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
