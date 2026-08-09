using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface ICategoriesApi
{
    [Get("/api/v1/categories")]
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [Post("/api/v1/categories")]
    Task<CategoryDto> CreateAsync([Body] CreateCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>System defaults answer 404 - they are shared by every account and are not the caller's to rename.</summary>
    [Put("/api/v1/categories/{id}")]
    Task<CategoryDto> RenameAsync(Guid id, [Body] CreateCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns how many of the caller's subscriptions the delete left uncategorised - they are not deleted with it.</summary>
    [Delete("/api/v1/categories/{id}")]
    Task<DeleteCategoryResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
