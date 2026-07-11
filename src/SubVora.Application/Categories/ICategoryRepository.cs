namespace SubVora.Application.Categories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CategoryDto> AddAsync(Guid userId, string name, CancellationToken cancellationToken = default);
}
