using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace Paddock.App.Views;

/// <summary>About Paddock, as a little terminal readout.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = $"v{version}";
        BuildText.Text = $"{version} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        RuntimeText.Text = $"{RuntimeInformation.FrameworkDescription} · {WindowsName()}";

        var app = (App)Application.Current;
        var pads = app.PaddockManager.GetPaddocks();
        StatusText.Text = $"online · {pads.Count} pads · {pads.Sum(p => p.Icons.Count)} icons";
    }

    /// <summary>"Windows 11 (26200)" — build 22000 and up is Windows 11.</summary>
    private static string WindowsName()
    {
        var build = Environment.OSVersion.Version.Build;
        return $"Windows {(build >= 22000 ? 11 : 10)} ({build})";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Core.Services.Log.Warn($"Could not open {e.Uri}: {ex.Message}");
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
            Close();
    }
}
