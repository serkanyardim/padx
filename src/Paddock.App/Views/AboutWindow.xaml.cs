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
        StatusText.Text = Helpers.Lang.F("online · {0} pads · {1} icons", pads.Count, pads.Sum(p => p.Icons.Count));
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

    // --- Updates ---

    private Core.Services.ReleaseInfo? _latest;

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        InstallButton.Visibility = Visibility.Collapsed;
        UpdateText.Text = "> updates     " + Helpers.Lang.T("checking github…");

        _latest = await Services.Updater.GetLatestAsync();
        CheckButton.IsEnabled = true;

        var current = Services.Updater.CurrentVersion;
        if (_latest is null)
        {
            UpdateText.Text = "> updates     " + Helpers.Lang.T("couldn't reach github — try again later");
        }
        else if (Core.Services.UpdateCheck.IsNewer(_latest.Version, current))
        {
            UpdateText.Text = "> updates     " + Helpers.Lang.F("v{0} available", _latest.Version.ToString(3));
            UpdateText.Foreground = (System.Windows.Media.Brush)FindResource("D.Accent");
            InstallButton.Visibility = _latest.SetupUrl is null ? Visibility.Collapsed : Visibility.Visible;
            CheckButton.Content = Helpers.Lang.T("RELEASE NOTES");
            CheckButton.Click -= Check_Click;
            CheckButton.Click += (_, _) => Open(_latest.PageUrl);
        }
        else
        {
            UpdateText.Text = "> updates     " + Helpers.Lang.F("✓ up to date (v{0})", current.ToString(3));
        }
    }

    /// <summary>Downloads the new installer, starts it and steps aside so it can replace padx.</summary>
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_latest is null)
            return;

        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        var progress = new Progress<int>(p => UpdateText.Text = "> updates     " + Helpers.Lang.F("downloading v{0}… {1}%", _latest.Version.ToString(3), p));

        if (await Services.Updater.DownloadAndRunAsync(_latest, progress))
        {
            // The installer closes and replaces padx; leave the way clear.
            Application.Current.Shutdown();
            return;
        }

        UpdateText.Text = "> updates     " + Helpers.Lang.T("download failed — get it from the release page");
        InstallButton.IsEnabled = CheckButton.IsEnabled = true;
    }

    private static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Core.Services.Log.Warn($"Could not open {url}: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
            Close();
    }
}
