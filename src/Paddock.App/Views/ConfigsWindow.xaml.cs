using System.Windows;
using System.Windows.Input;

namespace Paddock.App.Views;

/// <summary>
/// The saved configs, newest first: load one, mark one (★) to load at every
/// startup, or delete one.
/// </summary>
public partial class ConfigsWindow : Window
{
    public sealed record Row(string Name, DateTime SavedAt, bool IsStartup)
    {
        public Visibility StarVisibility => IsStartup ? Visibility.Visible : Visibility.Hidden;
    }

    public ConfigsWindow()
    {
        InitializeComponent();
        Refresh();
    }

    private static App CurrentApp => (App)Application.Current;

    private Row? Selected => List.SelectedItem as Row;

    private void Refresh(string? select = null)
    {
        var startup = CurrentApp.Settings.Settings.StartupConfig;
        var rows = CurrentApp.Configs.Store.List()
            .Select(c => new Row(c.Name, c.SavedAt, string.Equals(c.Name, startup, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Name == select) ?? rows.FirstOrDefault();
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StartupHint.Text = startup is null
            ? "startup: last session"
            : $"startup: ★ {startup}" + (rows.Any(r => r.IsStartup) ? string.Empty : "  (missing — last session is used)");

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = Selected;
        LoadButton.IsEnabled = DeleteButton.IsEnabled = StartupButton.IsEnabled = selected is not null;
        StartupButton.Content = selected?.IsStartup == true ? "☆ NO STARTUP" : "★ STARTUP";
        LoadButton.Opacity = DeleteButton.Opacity = StartupButton.Opacity = selected is null ? 0.4 : 1;
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelected();

    private void Load_Click(object sender, RoutedEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (Selected is not { } row)
            return;

        Close();
        CurrentApp.LoadConfig(row.Name, confirm: false);
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;

        var settings = CurrentApp.Settings;
        settings.Settings.StartupConfig = row.IsStartup ? null : row.Name;
        settings.Save();
        Refresh(row.Name);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;

        if (!DigitalDialog.Confirm("Delete config", $"Delete \"{row.Name}\"? This can't be undone.", "DELETE"))
            return;

        CurrentApp.Configs.Store.Delete(row.Name);

        if (row.IsStartup)
        {
            CurrentApp.Settings.Settings.StartupConfig = null;
            CurrentApp.Settings.Save();
        }

        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.Enter)
            LoadSelected();
    }
}
