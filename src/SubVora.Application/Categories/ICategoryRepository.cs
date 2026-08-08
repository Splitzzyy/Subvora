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

    /// <summary>
    /// Renames a category the user owns. Null when there is no such category <em>of theirs</em> -
    /// a system default (user_id IS NULL) is shared by every account and is never theirs to rename.
    /// </summary>
    Task<CategoryDto?> RenameAsync(Guid categoryId, Guid userId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a category the user owns, reporting how many of their subscriptions were left
    /// uncategorised by it. The foreign key is ON DELETE SET NULL, so the subscriptions survive -
    /// losing a grouping beats losing the record - but the caller has to be able to say so.
    /// </summary>
    Task<DeleteCategoryResult?> DeleteAsync(Guid categoryId, Guid userId, CancellationToken cancellationToken = default);
}
