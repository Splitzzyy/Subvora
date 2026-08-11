using Microsoft.Maui.Controls.Shapes;

namespace SubVora.Mobile.Views;

/// <summary>
/// The contents of the bottom sheet. Rows are built in code rather than bound to a collection
/// because the caller passes plain strings - <c>IUserPrompt.ActionSheetAsync</c> keeps the platform
/// action sheet's signature so view models and their tests are unaffected by this being a custom
/// sheet rather than the system one.
/// </summary>
public partial class ActionSheetView : ContentView
{
    /// <summary>
    /// Icons for the actions this app actually offers. An action without an entry simply gets no
    /// glyph and a text-only row - better than picking a wrong icon for a word we did not expect.
    /// </summary>
    private static readonly Dictionary<string, string> IconKeyByAction = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rename"] = "IconSliders",
        ["Delete"] = "IconTrash",
    };

    /// <summary>Actions that destroy something, drawn in the danger colour rather than the brand one.</summary>
    private static readonly HashSet<string> DestructiveActions = new(StringComparer.OrdinalIgnoreCase) { "Delete" };

    /// <summary>Raised with the chosen action. Never raised on dismissal - the popup handles that.</summary>
    public event EventHandler<string>? ActionChosen;

    public ActionSheetView(string title, IEnumerable<string> actions)
    {
        InitializeComponent();

        TitleLabel.Text = title;

        foreach (var action in actions)
        {
            ActionsLayout.Add(BuildRow(action));
        }
    }

    private View BuildRow(string action)
    {
        var isDestructive = DestructiveActions.Contains(action);

        var accent = isDestructive
            ? ThemeColor("DangerLight", "DangerDark")
            : ThemeColor("BrandPurple", "BrandPurpleLight");

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 16,
            Padding = new Thickness(14, 12),
        };

        if (IconKeyByAction.TryGetValue(action, out var iconKey)
            && Application.Current?.Resources.TryGetValue(iconKey, out var geometry) == true
            && geometry is Geometry icon)
        {
            var tile = new Border
            {
                WidthRequest = 40,
                HeightRequest = 40,
                Padding = 0,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = accent.WithAlpha(0.14f),
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                Content = new Microsoft.Maui.Controls.Shapes.Path
                {
                    Data = icon,
                    Aspect = Stretch.Uniform,
                    WidthRequest = 20,
                    HeightRequest = 20,
                    Fill = accent,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            };

            row.Add(tile);
            Grid.SetColumn(tile, 0);
        }

        var label = new Label
        {
            Text = action,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            TextColor = isDestructive ? accent : ThemeColor("TextStrongLight", "TextStrongDark"),
        };
        row.Add(label);
        Grid.SetColumn(label, 1);

        var container = new Border
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            BackgroundColor = isDestructive
                ? accent.WithAlpha(0.08f)
                : ThemeColor("SurfaceMutedLight", "SurfaceMutedDark"),
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Content = row,
        };

        // The whole row is the target, not just the label - a 40px icon beside text people aim at
        // is a tap that does nothing if only the text is wired up.
        container.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ActionChosen?.Invoke(this, action)),
        });

        return container;
    }

    /// <summary>
    /// Resolves a light/dark token pair for the current theme. The rows are built in code, so they
    /// cannot use AppThemeBinding markup and have to pick the side themselves.
    /// </summary>
    private static Color ThemeColor(string lightKey, string darkKey)
    {
        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;

        return Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Grey;
    }
}
