namespace SubVora.Application.Categories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CategoryDto> AddAsync(Guid userId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the category may be referenced by this user - a system default (user_id IS NULL) or
    /// one they own. Existence alone is not enough: the foreign key accepts any category id, so
    /// without this a request can attach a stranger's private category to its own subscription.
    /// </summary>
    Task<bool> IsAccessibleToUserAsync(Guid categoryId, Guid userId, CancellationToken cancellationToken = default);
}
