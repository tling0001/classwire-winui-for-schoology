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
        if (e.Parameter is UIElement content && !ReferenceEquals(Content, content))
            Content = content;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Detach the actual UIElement stored in the navigation parameter before
        // the page leaves the frame. Frame.GoBack can then reattach that view
        // without violating XAML's single-parent rule.
        base.OnNavigatedFrom(e);
        Content = null;
    }
}
