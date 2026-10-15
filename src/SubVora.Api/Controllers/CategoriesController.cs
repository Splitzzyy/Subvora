using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubVora.Application;
using SubVora.Application.Categories;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Controllers;

/// <summary>System default and user-owned subscription categories.</summary>
[Authorize]
[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IValidator<CreateCategoryRequest> _createValidator;

    public CategoriesController(ICategoryRepository categoryRepository, IValidator<CreateCategoryRequest> createValidator)
    {
        _categoryRepository = categoryRepository;
        _createValidator = createValidator;
    }

    /// <summary>Lists system default categories plus the authenticated user's own.</summary>
    /// <response code="200">Returns the available categories.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetForUserAsync(GetUserId(), cancellationToken);
        return Ok(categories);
    }

    /// <summary>Creates a custom category owned by the authenticated user.</summary>
    /// <response code="201">The category was created.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="409">The caller already has a category with this name.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var userId = GetUserId();
        var name = request.Name.Trim();

        // A name the user already has is not a new row - a retried create whose response was lost,
        // most likely - so it goes on to the insert and gets the same 409 it would below the cap.
        // ponytail: count-then-insert can overshoot by the number of concurrent creates; a hard
        // DB-side cap needs a trigger, not worth it for an abuse guard.
        if (await _categoryRepository.CountForUserAsync(userId, cancellationToken) >= UserQuotas.Categories
            && !await _categoryRepository.NameExistsForUserAsync(userId, name, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, $"You can have at most {UserQuotas.Categories} categories of your own.");
            return ValidationProblem(ModelState);
        }

        try
        {
            var category = await _categoryRepository.AddAsync(userId, name, cancellationToken);
            return CreatedAtAction(nameof(GetAll), category);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Conflict(new { message = "A category with this name already exists." });
        }
    }

    /// <summary>Renames a category the caller owns.</summary>
    /// <remarks>System default categories are shared by every account and cannot be renamed; they answer 404 like any other category the caller does not own.</remarks>
    /// <response code="200">Returns the renamed category.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such category owned by the caller.</response>
    /// <response code="409">The caller already has a category with this name.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        try
        {
            var renamed = await _categoryRepository.RenameAsync(id, GetUserId(), request.Name.Trim(), cancellationToken);
            return renamed is null ? NotFound() : Ok(renamed);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Same unique index Create trips over, for the same reason.
            return Conflict(new { message = "A category with this name already exists." });
        }
    }

    /// <summary>Deletes a category the caller owns.</summary>
    /// <remarks>
    /// Subscriptions using it are not deleted - they become uncategorised, since losing a grouping
    /// beats losing the record. The response reports how many were affected so a client can say so
    /// afterwards. System default categories cannot be deleted and answer 404.
    /// </remarks>
    /// <response code="200">The category was deleted; returns how many subscriptions were uncategorised.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">No such category owned by the caller.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(DeleteCategoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _categoryRepository.DeleteAsync(id, GetUserId(), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
