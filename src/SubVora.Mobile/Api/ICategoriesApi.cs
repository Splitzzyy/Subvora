using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface ICategoriesApi
{
    [Get("/api/v1/categories")]
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [Post("/api/v1/categories")]
    Task<CategoryDto> CreateAsync([Body] CreateCategoryRequest request, CancellationToken cancellationToken = default);
}
