using System.Windows;
using System.Windows.Input;
using Paddock.App.ViewModels;

namespace Paddock.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _viewModel = new SettingsViewModel(app.Settings);
        DataContext = _viewModel;
    }

    private static App CurrentApp => (App)Application.Current;

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.FlushSave();
        base.OnClosed(e);
    }

    // Escape inside a hotkey or colour field is handled there and never reaches this.
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RestoreAll_Click(object sender, RoutedEventArgs e) => CurrentApp.RestoreAllIcons();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => App.OpenSettingsFolder();

    private void StartupLayout_Click(object sender, RoutedEventArgs e)
    {
        var options = new List<string> { SettingsViewModel.LastSession };
        options.AddRange(CurrentApp.Configs.Store.List().Select(c => c.Name));

        var choice = DigitalDialog.Choose("Startup layout",
            options.Count == 1
                ? "No configs saved yet — use the tray menu: Config ▸ Save Config."
                : "Start with the last session, or load this config every time padx starts:",
            options,
            _viewModel.StartupLayout);

        if (choice is not null)
            _viewModel.StartupLayout = choice;
    }

    private void RunAutoOrganize_Click(object sender, RoutedEventArgs e) => CurrentApp.RunAutoOrganize();

    private void ResetAppearance_Click(object sender, RoutedEventArgs e) => _viewModel.ResetAppearance();
}
