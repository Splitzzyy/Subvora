using SQLite;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests;

public class SqliteLocalCacheServiceTests : IDisposable
{
    private class TestCacheItem
    {
        [PrimaryKey]
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    private readonly string _databasePath;
    private readonly SqliteLocalCacheService _cacheService;

    public SqliteLocalCacheServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"subvora_cache_test_{Guid.NewGuid():N}.db3");
        _cacheService = new SqliteLocalCacheService(_databasePath);
    }

    public void Dispose()
    {
        // Best-effort: SQLiteAsyncConnection may still hold the file handle briefly after
        // the last query completes, and this is just a scratch file under %TEMP%.
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task UpsertThenGetAll_RoundTripsThePoco()
    {
        await _cacheService.UpsertAsync(new TestCacheItem { Id = 1, Value = "first" });

        var items = await _cacheService.GetAllAsync<TestCacheItem>();

        var item = Assert.Single(items);
        Assert.Equal(1, item.Id);
        Assert.Equal("first", item.Value);
    }

    [Fact]
    public async Task SecondUpsertForSameKey_ReplacesRatherThanDuplicates()
    {
        await _cacheService.UpsertAsync(new TestCacheItem { Id = 1, Value = "first" });
        await _cacheService.UpsertAsync(new TestCacheItem { Id = 1, Value = "second" });

        var items = await _cacheService.GetAllAsync<TestCacheItem>();

        var item = Assert.Single(items);
        Assert.Equal("second", item.Value);
    }

    [Fact]
    public async Task ClearAsync_EmptiesTheTable()
    {
        await _cacheService.UpsertAsync(new TestCacheItem { Id = 1, Value = "first" });
        await _cacheService.UpsertAsync(new TestCacheItem { Id = 2, Value = "second" });

        await _cacheService.ClearAsync<TestCacheItem>();

        var items = await _cacheService.GetAllAsync<TestCacheItem>();
        Assert.Empty(items);
    }

    [Fact]
    public async Task ClearAllAsync_EmptiesEveryCachedType()
    {
        await _cacheService.UpsertAsync(new CachedBurnRate { Monthly = 42m, HomeCurrency = "USD" });
        await _cacheService.UpsertAsync(new CachedSubscription { Id = Guid.NewGuid(), CustomName = "Netflix" });

        await _cacheService.ClearAllAsync();

        Assert.Empty(await _cacheService.GetAllAsync<CachedBurnRate>());
        Assert.Empty(await _cacheService.GetAllAsync<CachedSubscription>());
    }

    /// <summary>
    /// Every settable property on <see cref="SubscriptionDto"/>, filled with a distinct non-default
    /// value. Reflection then checks that each one survives the round trip, so a property added to
    /// the DTO later fails here instead of being silently dropped by the mirror.
    /// </summary>
    private static SubscriptionDto FullyPopulatedDto() => new()
    {
        Id = Guid.NewGuid(),
        CustomName = "Netflix Premium",
        CostAmount = 649.50m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Quarterly,
        PurchaseDate = new DateOnly(2026, 1, 15),
        NextBillingDate = new DateOnly(2026, 4, 15),
        LastPaidDate = new DateOnly(2026, 1, 15),
        AlertDaysAdvance = 7,
        Version = 8_675_309u,
        CategoryId = Guid.NewGuid(),
        CategoryName = "Entertainment",
        PaymentSourceId = Guid.NewGuid(),
        PaymentSourceLabel = "HDFC Card",
        CatalogId = Guid.NewGuid(),
        CatalogLogoUrl = "https://cdn.simpleicons.org/netflix",
        IsFreeTrial = true,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public void CachedSubscription_MirrorsEveryPropertyOfTheDto()
    {
        // IsOverdue is computed from NextBillingDate and IsActive, so it has nothing to store.
        var excluded = new[] { nameof(SubscriptionDto.IsOverdue) };

        var dto = FullyPopulatedDto();
        var roundTripped = CachedSubscription.FromDto(dto).ToDto();

        var properties = typeof(SubscriptionDto).GetProperties()
            .Where(property => property.CanWrite && !excluded.Contains(property.Name))
            .ToList();

        // Guards the guard: if the DTO's shape changes so that nothing is enumerated, this test
        // would pass vacuously and stop protecting anything.
        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            var expected = property.GetValue(dto);
            var actual = property.GetValue(roundTripped);

            Assert.False(
                Equals(expected, property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null),
                $"{property.Name} was left at its default in FullyPopulatedDto, so the round trip is not actually tested.");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task CachedSubscription_SurvivesTheDatabaseRoundTripWithVersionAndCatalogId()
    {
        // Through real sqlite-net rather than just FromDto/ToDto: the two columns added for this are
        // a uint? and a Guid?, and it is the storage layer that has to accept them.
        var dto = FullyPopulatedDto();

        await _cacheService.UpsertAsync(CachedSubscription.FromDto(dto));
        var reloaded = Assert.Single(await _cacheService.GetAllAsync<CachedSubscription>()).ToDto();

        Assert.Equal(dto.Version, reloaded.Version);
        Assert.Equal(dto.CatalogId, reloaded.CatalogId);
    }

    [Fact]
    public void CachedSubscription_WrittenBeforeTheVersionColumnExisted_ReadsBackAsZeroNotAsGarbage()
    {
        // sqlite-net adds the column on CreateTableAsync but leaves pre-upgrade rows at null. The
        // mirror stores Version as uint? precisely so that state is representable.
        var row = new CachedSubscription
        {
            Id = Guid.NewGuid(),
            CustomName = "Written by an older build",
            Currency = "INR",
            Version = null,
            CatalogId = null,
        };

        var dto = row.ToDto();

        Assert.Equal(0u, dto.Version);
        Assert.Null(dto.CatalogId);
    }
}
