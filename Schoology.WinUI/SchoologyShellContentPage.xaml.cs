using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Schoology.WinUI;

public sealed partial class SchoologyShellContentPage : Page
{
    public SchoologyShellContentPage()
    {
        // This page has no XAML content; avoid loading a standalone ms-appx resource in unpackaged installs.
        NavigationCacheMode = NavigationCacheMode.Disabled;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is UIElement content)
            Content = content;
    }
}
