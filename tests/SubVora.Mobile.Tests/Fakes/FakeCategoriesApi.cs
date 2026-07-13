using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeCategoriesApi : ICategoriesApi
{
    public Func<Task<IReadOnlyList<CategoryDto>>> GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([]);
    public Func<CreateCategoryRequest, Task<CategoryDto>> CreateHandler =
        request => Task.FromResult(new CategoryDto { Id = Guid.NewGuid(), Name = request.Name, IsSystemDefault = false });

    public Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default) => GetAllHandler();

    public Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) => CreateHandler(request);
}
