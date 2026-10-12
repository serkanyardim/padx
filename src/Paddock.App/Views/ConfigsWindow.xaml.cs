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

    private readonly bool _saving;
    private bool _refreshing;

    /// <param name="saving">
    /// Opened from "Save Config": the name box has focus and clicking a saved
    /// config copies its name in, ready to overwrite it.
    /// </param>
    public ConfigsWindow(bool saving = false)
    {
        _saving = saving;
        InitializeComponent();

        NameBox.Text = Core.Services.ConfigStore.DefaultName(DateTime.Now);
        Refresh();

        Loaded += (_, _) =>
        {
            if (_saving)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
            else
            {
                List.Focus();
            }
        };
    }

    /// <summary>Switches an already open window into save mode.</summary>
    public void PrepareToSave()
    {
        NameBox.Text = Core.Services.ConfigStore.DefaultName(DateTime.Now);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveAs(NameBox.Text);

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveAs(NameBox.Text);
            e.Handled = true;
        }
    }

    private void SaveAs(string name)
    {
        name = name.Trim();

        if (Core.Services.ConfigStore.Validate(name) is { } error)
        {
            DigitalDialog.Inform("Save config", error);
            return;
        }

        if (CurrentApp.Configs.Store.Exists(name)
            && !DigitalDialog.Confirm("Save config", $"\"{name}\" already exists. Overwrite it with the current layout?", "OVERWRITE"))
        {
            return;
        }

        try
        {
            CurrentApp.Configs.Save(name);
            Refresh(name);
            DigitalDialog.Inform("Save config", $"Saved as \"{name}\".");
        }
        catch (Exception ex)
        {
            Core.Services.Log.Error($"Could not save config '{name}'.", ex);
            DigitalDialog.Inform("Save config", $"Could not save: {ex.Message}");
        }
    }

    private static App CurrentApp => (App)Application.Current;

    private Row? Selected => List.SelectedItem as Row;

    private void Refresh(string? select = null)
    {
        var startup = CurrentApp.Settings.Settings.StartupConfig;
        var rows = CurrentApp.Configs.Store.List()
            .Select(c => new Row(c.Name, c.SavedAt, string.Equals(c.Name, startup, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _refreshing = true;
        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Name == select) ?? rows.FirstOrDefault();
        _refreshing = false;
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

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateButtons();

        // In save mode a click on a saved config means "overwrite this one".
        if (_saving && !_refreshing && e.AddedItems.Count > 0 && Selected is { } row)
            NameBox.Text = row.Name;
    }

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
        else if (e.Key == Key.Enter && !NameBox.IsKeyboardFocused)
            LoadSelected();
    }
}
