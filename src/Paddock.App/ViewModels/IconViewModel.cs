using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.ViewModels;

public class IconViewModel : INotifyPropertyChanged
{
    private readonly IconEntry _entry;
    private ImageSource? _iconImage;

    public IconViewModel(IconEntry entry)
        : this(entry, ((App)Application.Current).IconExtractor)
    {
    }

    public IconViewModel(IconEntry entry, IconExtractor iconExtractor)
    {
        _entry = entry;

        // Computed once: bindings re-evaluate these, and working out whether the
        // item is a folder costs a filesystem call each time.
        DisplayName = Directory.Exists(entry.DesktopPath)
            ? Path.GetFileName(entry.DesktopPath)
            : Path.GetFileNameWithoutExtension(entry.DesktopPath);

        ToolTipText = $"{Path.GetFileName(entry.DesktopPath)}\n{entry.DesktopPath}";

        BeginLoadIcon(iconExtractor);
    }

    /// <summary>
    /// Label shown under the icon. Extensions are dropped the way the desktop
    /// does it, but a folder called "My.Stuff" keeps its whole name.
    /// </summary>
    public string DisplayName { get; }

    public string FullPath => _entry.DesktopPath;

    /// <summary>Tooltip: the real file name, plus where it actually lives.</summary>
    public string ToolTipText { get; }

    /// <summary>True when Paddock moved this item off the desktop.</summary>
    public bool IsManaged => _entry.Managed;

    /// <summary>
    /// The item's icon. Filled in asynchronously — a paddock draws immediately
    /// and its icons appear as the shell hands them over.
    /// </summary>
    public ImageSource? IconImage => _iconImage;

    private bool _isSpotlit;

    /// <summary>Highlighted for a moment, after the desktop search pointed at it.</summary>
    public bool IsSpotlit
    {
        get => _isSpotlit;
        set
        {
            if (_isSpotlit == value)
                return;

            _isSpotlit = value;
            OnPropertyChanged();
        }
    }

    public void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _entry.DesktopPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_entry.DesktopPath) ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to launch '{_entry.DesktopPath}'.", ex);
        }
    }

    private void BeginLoadIcon(IconExtractor iconExtractor)
    {
        // Already known: use it straight away so nothing flickers on a refresh.
        if (iconExtractor.TryGetCached(_entry.DesktopPath, out var cached))
        {
            _iconImage = cached;
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;

        iconExtractor.RequestIcon(_entry.DesktopPath, icon =>
        {
            if (icon is null)
                return;

            // No dispatcher, or one that is already going away at shutdown:
            // set the field directly rather than throwing on a worker thread.
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                _iconImage = icon;
                return;
            }

            dispatcher.InvokeAsync(() =>
            {
                _iconImage = icon;
                OnPropertyChanged(nameof(IconImage));
            });
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
