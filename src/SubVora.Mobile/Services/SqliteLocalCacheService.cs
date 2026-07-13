using SQLite;

namespace SubVora.Mobile.Services;

/// <summary>
/// Read-only cache mirror, not a two-way sync engine: refreshed after every successful GET,
/// with no write-queueing or conflict resolution.
/// </summary>
public class SqliteLocalCacheService : ILocalCacheService
{
    private readonly SQLiteAsyncConnection _connection;

    public SqliteLocalCacheService(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task UpsertAsync<T>(T item) where T : class, new()
    {
        await _connection.CreateTableAsync<T>();
        await _connection.InsertOrReplaceAsync(item);
    }

    public async Task<List<T>> GetAllAsync<T>() where T : class, new()
    {
        await _connection.CreateTableAsync<T>();
        return await _connection.Table<T>().ToListAsync();
    }

    public async Task ClearAsync<T>() where T : class, new()
    {
        await _connection.CreateTableAsync<T>();
        await _connection.DeleteAllAsync<T>();
    }
}
