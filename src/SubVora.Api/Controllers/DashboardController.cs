using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Dashboard;
using SubVora.Application.Subscriptions;
using SubVora.Application.Users;

namespace SubVora.Api.Controllers;

/// <summary>Aggregate spend views across a user's subscriptions.</summary>
[Authorize]
[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IBurnRateCalculator _burnRateCalculator;
    private readonly IUserRepository _userRepository;

    public DashboardController(ISubscriptionRepository subscriptionRepository, IBurnRateCalculator burnRateCalculator, IUserRepository userRepository)
    {
        _subscriptionRepository = subscriptionRepository;
        _burnRateCalculator = burnRateCalculator;
        _userRepository = userRepository;
    }

    /// <summary>Weekly/monthly/yearly recurring spend, plus one-time purchases this year.</summary>
    /// <remarks>
    /// Every subscription's native-currency cost is converted to the caller's preferred_currency via
    /// cached FX rates before summing. A subscription whose currency pair has no cached rate is
    /// excluded from the totals and reported via UnresolvedSubscriptionIds rather than failing the
    /// whole request. Active free trials and one-time purchases are excluded from the recurring totals
    /// and from the per-category breakdown (ByCategory); subscriptions with no assigned category are
    /// grouped under "Uncategorized".
    /// </remarks>
    /// <response code="200">Returns the burn-rate totals.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet("burn-rate")]
    [ProducesResponseType(typeof(BurnRateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBurnRate(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var preferredCurrency = await _userRepository.GetPreferredCurrencyAsync(userId, cancellationToken) ?? "USD";
        var subscriptions = await _subscriptionRepository.GetAllForUserAsync(userId, cancellationToken);
        var result = await _burnRateCalculator.CalculateAsync(subscriptions, preferredCurrency, cancellationToken);
        return Ok(result);
    }
}
