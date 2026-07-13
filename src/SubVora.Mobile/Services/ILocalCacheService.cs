namespace SubVora.Mobile.Services;

public interface ILocalCacheService
{
    Task UpsertAsync<T>(T item) where T : class, new();
    Task<List<T>> GetAllAsync<T>() where T : class, new();
    Task ClearAsync<T>() where T : class, new();
}
