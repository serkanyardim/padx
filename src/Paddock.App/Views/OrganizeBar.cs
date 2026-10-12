using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Paddock.App.Helpers;

namespace Paddock.App.Views;

/// <summary>
/// Floating bar in the bottom-right corner after an Auto Organize: keep the
/// new layout, or put everything back the way it was.
/// </summary>
internal sealed class OrganizeBar : Window
{
    public OrganizeBar(string summary, Action accept, Action revert)
    {
        Title = "Paddock";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;

        var revertButton = new Button { Content = "REVERT", Style = (Style)FindResource("D.Button"), Margin = new Thickness(0, 0, 8, 0) };
        revertButton.Click += (_, _) => revert();

        var acceptButton = new Button { Content = "ACCEPT", Style = (Style)FindResource("D.Button"), Foreground = (Brush)FindResource("D.Accent") };
        acceptButton.Click += (_, _) => accept();

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        text.Children.Add(new TextBlock
        {
            Text = "AUTO ORGANIZE",
            FontFamily = (FontFamily)FindResource("D.Mono"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("D.Accent"),
        });
        text.Children.Add(new TextBlock
        {
            Text = summary,
            FontFamily = (FontFamily)FindResource("D.Mono"),
            FontSize = 10,
            Foreground = (Brush)FindResource("D.TextDim"),
            Margin = new Thickness(0, 3, 0, 0),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Width = 3, Background = (Brush)FindResource("D.Accent"), Margin = new Thickness(0, 0, 14, 0) });
        row.Children.Add(text);
        row.Children.Add(revertButton);
        row.Children.Add(acceptButton);

        Content = new Border
        {
            Background = (Brush)FindResource("D.Panel"),
            BorderBrush = (Brush)FindResource("D.LineStrong"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 12, 14, 12),
            Child = row,
        };

        Loaded += (_, _) => PlaceInCorner();
    }

    private void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 20;
        Top = area.Bottom - ActualHeight - 20;
    }
}
