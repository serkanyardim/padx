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
    private TextBox? _input;

    private DigitalDialog(string title, string message, string confirmText, string? cancelText, string? inputText = null, int selectLength = -1)
    {
        // Callers pass English; fixed texts are translated here, built ones arrive translated.
        title = Helpers.Lang.T(title);
        message = Helpers.Lang.T(message);
        confirmText = Helpers.Lang.T(confirmText);
        cancelText = cancelText is null ? null : Helpers.Lang.T(cancelText);

        Title = "padx";
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
            Text = Helpers.Lang.Upper(title),
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
        if (inputText is not null)
        {
            _input = new TextBox
            {
                Text = inputText,
                Style = (Style)FindResource("D.Field"),
                FontSize = 13,
                Margin = new Thickness(0, 12, 0, 0),
            };
            body.Children.Add(_input);

            // Select the name, not the extension — as Explorer does.
            Loaded += (_, _) =>
            {
                _input.Focus();
                _input.Select(0, selectLength >= 0 ? selectLength : inputText.Length);
            };
        }

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

    /// <summary>Lets the user pick one of <paramref name="options"/>; null when cancelled.</summary>
    public static string? Choose(string title, string message, IReadOnlyList<string> options, string? selected)
    {
        var dialog = new DigitalDialog(title, message, "SELECT", "CANCEL");

        var list = new ListBox
        {
            ItemsSource = options,
            SelectedItem = selected ?? options.FirstOrDefault(),
            MaxHeight = 260,
            Margin = new Thickness(0, 12, 0, 0),
            Background = (Brush)dialog.FindResource("D.Bg"),
            BorderBrush = (Brush)dialog.FindResource("D.LineStrong"),
            Foreground = (Brush)dialog.FindResource("D.Text"),
            FontFamily = (FontFamily)dialog.FindResource("D.Mono"),
            FontSize = 12,
            Padding = new Thickness(2),
        };
        list.MouseDoubleClick += (_, _) => { dialog._confirmed = true; dialog.Close(); };

        // Insert the list above the buttons.
        var body = (StackPanel)((Border)dialog.Content).Child;
        body.Children.Insert(body.Children.Count - 1, list);
        dialog.Loaded += (_, _) => list.Focus();

        dialog.ShowDialog();
        return dialog._confirmed ? list.SelectedItem as string : null;
    }

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    public static string? Prompt(string title, string message, string initial, int selectLength = -1, string confirmText = "OK")
    {
        var dialog = new DigitalDialog(title, message, confirmText, "CANCEL", initial, selectLength);
        dialog.ShowDialog();
        return dialog._confirmed ? dialog._input!.Text : null;
    }

    public static void Inform(string title, string message)
        => new DigitalDialog(title, message, "OK", null).ShowDialog();
}
