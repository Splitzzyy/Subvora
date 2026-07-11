using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubVora.Application.Dashboard;
using SubVora.Application.Subscriptions;

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

    public DashboardController(ISubscriptionRepository subscriptionRepository, IBurnRateCalculator burnRateCalculator)
    {
        _subscriptionRepository = subscriptionRepository;
        _burnRateCalculator = burnRateCalculator;
    }

    /// <summary>Weekly/monthly/yearly recurring spend, plus one-time purchases this year.</summary>
    /// <remarks>
    /// Single-currency for now (each subscription's native currency, unconverted) - home-currency
    /// conversion via cached FX rates lands in a later slice. Active free trials and one-time
    /// purchases are excluded from the recurring totals.
    /// </remarks>
    /// <response code="200">Returns the burn-rate totals.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet("burn-rate")]
    [ProducesResponseType(typeof(BurnRateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBurnRate(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var subscriptions = await _subscriptionRepository.GetAllForUserAsync(userId, cancellationToken);
        var result = _burnRateCalculator.Calculate(subscriptions);
        return Ok(result);
    }
}
