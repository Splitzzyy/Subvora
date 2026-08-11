using Android.Content;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace SubVora.Mobile.Platforms.Android;

/// <summary>
/// Lets the tab icons keep their own colours.
/// <para>
/// Android's BottomNavigationView applies an <c>ItemIconTintList</c> to every tab icon, built from
/// Shell's TabBarForegroundColor and TabBarUnselectedColor. That tint replaces whatever colour the
/// icon actually has, so the five coloured tab SVGs all came out as one hue - purple when selected,
/// grey otherwise. Clearing the tint list is the only way to let a multi-coloured icon through.
/// </para>
/// <para>
/// Icons only. The labels still take TabBarTitleColor, so the selected tab is still marked by its
/// text turning brand purple - colour alone never carries "which tab am I on".
/// </para>
/// </summary>
public class ColorfulTabsShellRenderer : ShellRenderer
{
    public ColorfulTabsShellRenderer(Context context)
        : base(context)
    {
    }

    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem) =>
        new UntintedBottomNavAppearanceTracker(this, shellItem);

    private sealed class UntintedBottomNavAppearanceTracker : ShellBottomNavViewAppearanceTracker
    {
        public UntintedBottomNavAppearanceTracker(IShellContext shellContext, ShellItem shellItem)
            : base(shellContext, shellItem)
        {
        }

        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);

            // After base, which is what sets the tint in the first place.
            bottomView.ItemIconTintList = null;
        }

        public override void ResetAppearance(BottomNavigationView bottomView)
        {
            base.ResetAppearance(bottomView);
            bottomView.ItemIconTintList = null;
        }
    }
}
