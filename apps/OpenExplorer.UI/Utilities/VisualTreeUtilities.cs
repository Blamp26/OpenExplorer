using Microsoft.UI.Xaml;

namespace OpenExplorer_UI.Utilities;

public static class VisualTreeUtilities
{
    public static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
