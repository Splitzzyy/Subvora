namespace SubVora.Mobile.Api.Dtos;

public class CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystemDefault { get; set; }
}

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>What a category delete did. Subscriptions survive it - they just lose their grouping.</summary>
public class DeleteCategoryResult
{
    public int SubscriptionsUncategorized { get; set; }
}
