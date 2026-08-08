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

    public Func<Guid, CreateCategoryRequest, Task<CategoryDto>> RenameHandler =
        (id, request) => Task.FromResult(new CategoryDto { Id = id, Name = request.Name, IsSystemDefault = false });

    public Func<Guid, Task<DeleteCategoryResult>> DeleteHandler =
        _ => Task.FromResult(new DeleteCategoryResult { SubscriptionsUncategorized = 0 });

    public List<(Guid Id, CreateCategoryRequest Request)> RenameCalls { get; } = [];
    public List<Guid> DeleteCalls { get; } = [];

    public Task<CategoryDto> RenameAsync(Guid id, CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        RenameCalls.Add((id, request));
        return RenameHandler(id, request);
    }

    public Task<DeleteCategoryResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add(id);
        return DeleteHandler(id);
    }
}
