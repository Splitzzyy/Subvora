using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>In-memory ILocalCacheService fake. Upsert replaces the whole set for T, matching
/// the singleton-row semantics of cache models like CachedBurnRate.</summary>
public class FakeLocalCacheService : ILocalCacheService
{
    private readonly Dictionary<Type, List<object>> _store = [];

    public Task UpsertAsync<T>(T item) where T : class, new()
    {
        _store[typeof(T)] = [item];
        return Task.CompletedTask;
    }

    public Task<List<T>> GetAllAsync<T>() where T : class, new()
    {
        var items = _store.TryGetValue(typeof(T), out var list) ? list.Cast<T>().ToList() : [];
        return Task.FromResult(items);
    }

    public Task ClearAsync<T>() where T : class, new()
    {
        _store.Remove(typeof(T));
        return Task.CompletedTask;
    }
}
