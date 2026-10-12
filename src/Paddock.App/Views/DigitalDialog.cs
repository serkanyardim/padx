using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Paddock.App.Views;

/// <summary>
/// A message box in the app's "digital" style. Enter confirms, Esc cancels.
/// </summary>
internal sealed class DigitalDialog : Window
{
    private bool _confirmed;

    private DigitalDialog(string title, string message, string confirmText, string? cancelText)
    {
        Title = "Paddock";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        Background = Brush("D.Bg");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };

        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, Style = (Style)FindResource("D.Button"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);
        }

        var ok = new Button { Content = confirmText, Style = (Style)FindResource("D.Button"), IsDefault = true, Foreground = Brush("D.Accent") };
        ok.Click += (_, _) => { _confirmed = true; Close(); };
        buttons.Children.Add(ok);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new Border { Width = 8, Height = 8, Background = Brush("D.Accent"), Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = (FontFamily)FindResource("D.Mono"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("D.Text"),
        });

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            FontFamily = (FontFamily)FindResource("D.Sans"),
            FontSize = 13,
            Foreground = Brush("D.TextDim"),
        });
        body.Children.Add(buttons);

        Content = new Border { BorderBrush = Brush("D.LineStrong"), BorderThickness = new Thickness(1), Child = body };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    /// <summary>Asks a yes/no question; true when confirmed.</summary>
    public static bool Confirm(string title, string message, string confirmText = "CONTINUE", string cancelText = "CANCEL")
    {
        var dialog = new DigitalDialog(title, message, confirmText, cancelText);
        dialog.ShowDialog();
        return dialog._confirmed;
    }

    public static void Inform(string title, string message)
        => new DigitalDialog(title, message, "OK", null).ShowDialog();
}
