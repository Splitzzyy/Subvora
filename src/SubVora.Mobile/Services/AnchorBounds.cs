namespace SubVora.Mobile.Services;

/// <summary>
/// Screen position of a control, for anchoring a popup to it.
/// <para>
/// MAUI has no cross-platform "where is this element on screen" API - <c>VisualElement.Bounds</c> is
/// relative to the parent, which is useless inside a scrolled list. The platform view knows, so this
/// asks it.
/// </para>
/// </summary>
public static class AnchorBounds
{
    /// <summary>
    /// The element's bounds in device-independent units, measured from the top-left of the screen
    /// including the status bar - the same origin the popup overlay uses.
    /// <para>
    /// Null when the platform is not handled or the element is not realised yet. Callers treat null
    /// as "no anchor" and fall back to a fixed corner rather than guessing a position.
    /// </para>
    /// </summary>
    public static Rect? OnScreen(VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is not Android.Views.View native)
        {
            return null;
        }

        var location = new int[2];
        native.GetLocationOnScreen(location);

        // GetLocationOnScreen answers in physical pixels; everything in MAUI layout is in DIPs.
        var density = native.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0)
        {
            return null;
        }

        return new Rect(
            location[0] / density,
            location[1] / density,
            native.Width / density,
            native.Height / density);
#else
        // iOS/Windows would each need their own platform call. Neither is distributed today, and a
        // wrong position is worse than the unanchored fallback.
        _ = element;
        return null;
#endif
    }
}
