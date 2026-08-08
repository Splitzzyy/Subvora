namespace SubVora.Application.Categories;

/// <summary>
/// What a delete actually did. The count is the point: category_id is ON DELETE SET NULL, so
/// removing a category silently uncategorises the subscriptions that used it, and the user is
/// entitled to know how many before and after it happens.
/// </summary>
/// <param name="SubscriptionsUncategorized">How many of the caller's subscriptions lost their category.</param>
public sealed record DeleteCategoryResult(int SubscriptionsUncategorized);
