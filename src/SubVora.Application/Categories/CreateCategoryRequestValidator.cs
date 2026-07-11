using FluentValidation;

namespace SubVora.Application.Categories;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
    }
}
