using Microsoft.EntityFrameworkCore;
using SubVora.Application.Categories;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _dbContext;

    public CategoryRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _dbContext.Categories.AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, IsSystemDefault = c.UserId == null })
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> AddAsync(Guid userId, string name, CancellationToken cancellationToken = default)
    {
        var category = new Category { UserId = userId, Name = name, CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CategoryDto { Id = category.Id, Name = category.Name, IsSystemDefault = false };
    }
}
