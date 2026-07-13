using SQLite;
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
}
