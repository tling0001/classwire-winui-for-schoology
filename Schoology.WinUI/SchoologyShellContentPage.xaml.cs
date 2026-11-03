using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Schoology.WinUI;

public sealed partial class SchoologyShellContentPage : Page
{
    public SchoologyShellContentPage()
    {
        // Keep the shell host transparent; Page's default theme background otherwise
        // paints an opaque system surface over Mica and every acrylic child surface.
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        NavigationCacheMode = NavigationCacheMode.Disabled;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is UIElement content)
            Content = content;
    }
}
