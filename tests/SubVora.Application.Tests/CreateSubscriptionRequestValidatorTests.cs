using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Application.Tests;

public class CreateSubscriptionRequestValidatorTests
{
    private readonly CreateSubscriptionRequestValidator _validator = new();

    private static CreateSubscriptionRequest ValidRequest() => new()
    {
        CustomName = "Netflix Premium",
        CostAmount = 19.99m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 8, 1),
    };

    [Fact]
    public void CostAmount_AboveWhatTheColumnHolds_Fails()
    {
        // cost_amount is numeric(12,2). Without this bound the value passed validation and blew up
        // in Postgres with SQLSTATE 22003, which surfaced as a 500 rather than a 400.
        var request = ValidRequest();
        request.CostAmount = 10_000_000_000m;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSubscriptionRequest.CostAmount));
    }

    [Fact]
    public void CostAmount_AtTheTopOfWhatTheColumnHolds_Passes()
    {
        var request = ValidRequest();
        request.CostAmount = 9_999_999_999.99m;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CostAmount_NotPositive_Fails(decimal costAmount)
    {
        var request = ValidRequest();
        request.CostAmount = costAmount;

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void AlertDaysAdvance_BeyondAYear_Fails()
    {
        // RenewalNotificationPlanner does NextBillingDate.AddDays(-AlertDaysAdvance), which throws
        // near int.MaxValue. The scheduler catches it, so the app silently schedules no reminders
        // at all - a quiet loss of the feature rather than a crash.
        var request = ValidRequest();
        request.AlertDaysAdvance = int.MaxValue;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSubscriptionRequest.AlertDaysAdvance));
    }

    [Fact]
    public void AlertDaysAdvance_AtAYear_Passes()
    {
        var request = ValidRequest();
        request.AlertDaysAdvance = 365;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void AlertDaysAdvance_Omitted_Passes()
    {
        // Null means "use my global default", so the bound must not turn absence into an error.
        var request = ValidRequest();
        request.AlertDaysAdvance = null;

        Assert.True(_validator.Validate(request).IsValid);
    }
}
