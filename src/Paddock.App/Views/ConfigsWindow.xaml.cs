using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Paddock.App.Helpers;
using Paddock.App.Services;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// Config manager: save the current layout, load, overwrite or delete a
/// config, and mark one ★ default per main-screen resolution. Actions report
/// in a short status line instead of dialogs; only Delete asks first.
/// </summary>
public partial class ConfigsWindow : Window
{
    /// <param name="DefaultFor">The resolutions this config is the ★ default of.</param>
    public sealed record Row(string Name, DateTime SavedAt, string? Resolution, IReadOnlyList<string> DefaultFor, bool MatchesScreen)
    {
        public Visibility StarVisibility => DefaultFor.Count > 0 ? Visibility.Visible : Visibility.Hidden;

        public string StarTip => Lang.T("default for ") + string.Join(", ", DefaultFor.Select(ConfigStore.Display));

        /// <summary>Older configs have no resolution of their own; show the one they are the default of.</summary>
        public string ResolutionText => ConfigStore.Display(Resolution ?? DefaultFor.FirstOrDefault());

        /// <summary>Configs made for the screen in use stand out.</summary>
        public Brush ResolutionBrush => (Brush)Application.Current.FindResource(MatchesScreen ? "D.Accent" : "D.TextDim");
    }

    public ConfigsWindow()
    {
        InitializeComponent();
        NameBox.Text = ConfigStore.DefaultName(DateTime.Now);
        Refresh();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private static App CurrentApp => (App)Application.Current;

    private static ConfigManager Configs => CurrentApp.Configs;

    private Row? Selected => List.SelectedItem as Row;

    private void Refresh(string? select = null)
    {
        var screen = ScreenHelper.MainResolution();
        var defaults = CurrentApp.Settings.Settings.ResolutionDefaults;

        var rows = Configs.Store.List()
            .Select(c => new Row(
                c.Name,
                c.SavedAt,
                c.Resolution,
                defaults.Where(d => string.Equals(d.Value, c.Name, StringComparison.OrdinalIgnoreCase)).Select(d => d.Key).ToList(),
                string.Equals(c.Resolution, screen, StringComparison.OrdinalIgnoreCase)
                || (c.Resolution is null && string.Equals(defaults.GetValueOrDefault(screen), c.Name, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var keep = select ?? Selected?.Name;
        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Name == keep);
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ScreenText.Text = ConfigStore.Display(screen);
        DefaultText.Text = Configs.DefaultFor(screen) is { } current ? $"★ {current}" : Lang.T("none — pads just move to free space");
        SaveHint.Text = Lang.F("saved for {0} · a name that exists is replaced", ConfigStore.Display(screen));

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = Selected;
        foreach (var button in new[] { LoadButton, DefaultButton, OverwriteButton, DeleteButton })
        {
            button.IsEnabled = selected is not null;
            button.Opacity = selected is null ? 0.4 : 1;
        }

        DefaultButton.Content = Lang.T(selected?.DefaultFor.Count > 0 ? "☆ REMOVE DEFAULT" : "★ SET DEFAULT");
    }

    /// <summary>Feedback that fades out after a few seconds — no dialog to dismiss.</summary>
    private void Flash(string message, bool problem = false)
    {
        Status.Text = message;
        Status.Foreground = (Brush)FindResource(problem ? "D.Danger" : "D.Accent");

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.6))));
        Status.BeginAnimation(OpacityProperty, fade);
    }

    // --- Save ---

    private void Save_Click(object sender, RoutedEventArgs e) => SaveAs(NameBox.Text);

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        SaveAs(NameBox.Text);
        e.Handled = true;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            SaveAs(row.Name);
    }

    private async void SaveAs(string name)
    {
        name = name.Trim();
        if (ConfigStore.Validate(name) is { } error)
        {
            Flash("✗ " + error, problem: true);
            return;
        }

        var replacing = Configs.Store.Exists(name);

        try
        {
            IsEnabled = false;
            await Configs.SaveAsync(name);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not save config '{name}'.", ex);
            Flash(Lang.F("✗ could not save: {0}", ex.Message), problem: true);
            return;
        }
        finally
        {
            IsEnabled = true;
        }

        Refresh(name);
        NameBox.Text = ConfigStore.DefaultName(DateTime.Now);
        Flash(Lang.F(replacing ? "✓ replaced \"{0}\" for {1}" : "✓ saved \"{0}\" for {1}", name, ConfigStore.Display(ScreenHelper.MainResolution())));
    }

    // --- Load ---

    private void Load_Click(object sender, RoutedEventArgs e) => LoadSelected();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (Selected is not { } row)
            return;

        var missing = CurrentApp.LoadConfig(row.Name, confirm: false, quiet: true);
        Activate();
        Refresh(row.Name);

        if (missing is null)
            Flash(Lang.F("✗ \"{0}\" could not be read", row.Name), problem: true);
        else if (missing > 0)
            Flash(Lang.F("✓ loaded \"{0}\" — {1} item(s) no longer on this PC were skipped", row.Name, missing));
        else
            Flash(Lang.F("✓ loaded \"{0}\"", row.Name));
    }

    // --- Defaults ---

    /// <summary>
    /// ★ makes the selected config the default of the resolution it was saved
    /// at (older configs without one: the screen in use), replacing that
    /// resolution's previous default. ☆ removes it.
    /// </summary>
    private void Default_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;

        if (row.DefaultFor.Count > 0)
        {
            Configs.ForgetDefaults(row.Name);
            Refresh(row.Name);
            Flash(Lang.F("☆ \"{0}\" is no longer a default", row.Name));
            return;
        }

        var resolution = row.Resolution ?? ScreenHelper.MainResolution();
        var previous = Configs.DefaultFor(resolution);
        Configs.SetDefault(resolution, row.Name);
        Refresh(row.Name);
        Flash(Lang.F("★ \"{0}\" is the default for {1}", row.Name, ConfigStore.Display(resolution))
              + (previous is not null && previous != row.Name ? Lang.F(" (was \"{0}\")", previous) : string.Empty));
    }

    // --- Delete ---

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;

        if (!DigitalDialog.Confirm("Delete config", Lang.F("Delete \"{0}\"? This can't be undone.", row.Name), "DELETE"))
            return;

        Configs.Store.Delete(row.Name);
        Configs.ForgetDefaults(row.Name);
        Refresh();
        Flash(Lang.F("✓ deleted \"{0}\"", row.Name));
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.Enter && !NameBox.IsKeyboardFocused)
            LoadSelected();
    }
}
