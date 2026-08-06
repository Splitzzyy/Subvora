namespace SubVora.Mobile.Services;

public interface ILocalCacheService
{
    Task UpsertAsync<T>(T item) where T : class, new();
    Task<List<T>> GetAllAsync<T>() where T : class, new();
    Task ClearAsync<T>() where T : class, new();

    /// <summary>
    /// Empties every cached type. The single teardown point for "the session ended" - explicit
    /// sign-out and token expiry both route through here, so a new cached model only has to be
    /// listed in the implementation rather than at each call site.
    /// </summary>
    Task ClearAllAsync();
}
