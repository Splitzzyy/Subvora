using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.ViewModels;

/// <summary>
/// One section of the Categories list. There are exactly two - the seeded defaults every account
/// shares, and the ones this user created - and they are different kinds of thing: only the second
/// can be renamed or deleted, and the server enforces that regardless of what the UI offers.
/// <para>
/// Derives from List rather than wrapping one for the same reason <see cref="SubscriptionGroup"/>
/// does: CollectionView's grouping wants a collection of collections.
/// </para>
/// </summary>
public class CategoryGroup : List<CategoryDto>
{
    public const string SystemTitle = "System";
    public const string UserTitle = "Your categories";

    public CategoryGroup(string title, bool isSystem, IEnumerable<CategoryDto> categories)
        : base(categories)
    {
        Title = title;
        IsSystem = isSystem;
    }

    public string Title { get; }

    /// <summary>
    /// Whether this is the shared, read-only section. Drives whether rows offer the manage action -
    /// the heading already says which kind these are, so the rows themselves carry no badge.
    /// </summary>
    public bool IsSystem { get; }

    /// <summary>Right-hand side of the group header, matching the subscription list's house style.</summary>
    public string Summary => Count == 1 ? "1 category" : $"{Count} categories";
}
