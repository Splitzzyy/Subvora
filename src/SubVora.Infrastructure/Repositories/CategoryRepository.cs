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

    public async Task<CategoryDto?> RenameAsync(Guid categoryId, Guid userId, string name, CancellationToken cancellationToken = default)
    {
        // c.UserId == userId, not the GetForUserAsync predicate: seeing a system default is not
        // owning it, and renaming one would change it for every account on the instance.
        var category = await _dbContext.Categories
            .SingleOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.Name = name;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CategoryDto { Id = category.Id, Name = category.Name, IsSystemDefault = false };
    }

    public async Task<DeleteCategoryResult?> DeleteAsync(Guid categoryId, Guid userId, CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .SingleOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId, cancellationToken);
        if (category is null)
        {
            return null;
        }

        // Counted before the delete, because afterwards there is nothing left to count by: the
        // foreign key nulls the column out, so the association is gone rather than recorded.
        var affected = await _dbContext.UserSubscriptions
            .CountAsync(s => s.UserId == userId && s.CategoryId == categoryId, cancellationToken);

        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteCategoryResult(affected);
    }

    // Same predicate as GetForUserAsync - what a user may reference is exactly what a user may see.
    public Task<bool> IsAccessibleToUserAsync(Guid categoryId, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Categories.AsNoTracking()
            .AnyAsync(c => c.Id == categoryId && (c.UserId == null || c.UserId == userId), cancellationToken);
}
