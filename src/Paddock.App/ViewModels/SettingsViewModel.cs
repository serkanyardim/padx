using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Paddock.App.Helpers;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.App.ViewModels;

/// <summary>
/// Backs the settings window. Appearance changes are applied to the live pads
/// immediately; writing settings.json is debounced so dragging a slider does
/// not rewrite the file on every tick.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Everything the live preview binds to, refreshed on any appearance change.</summary>
    private static readonly string[] PreviewProperties =
    [
        nameof(PreviewBackground), nameof(PreviewBorderBrush), nameof(PreviewBorderThickness),
        nameof(PreviewCornerRadius), nameof(PreviewTitleBarBrush), nameof(PreviewTitleBarCornerRadius),
        nameof(PreviewTitleBrush), nameof(PreviewTitleFont), nameof(PreviewTitleAlignment),
        nameof(PreviewTitle), nameof(PreviewLabelBrush), nameof(PreviewLabelVisibility),
        nameof(PreviewIconSize), nameof(PreviewCellWidth), nameof(PreviewSheen),
        nameof(PreviewSheenVisibility), nameof(PreviewBlurVisibility), nameof(PreviewBlurRadius),
        nameof(IsGlass), nameof(GlassOptionsOpacity),
    ];

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _saveTimer;
    private string _hotkeyStatus = string.Empty;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushSave();
    }

    private static App CurrentApp => (App)Application.Current;

    private AppSettings S => _settings.Settings;

    private PadAppearance A => _settings.Settings.Appearance;

    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";

    // ===== General =====

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set
        {
            S.StartWithWindows = value;
            CurrentApp.StartupManager.SetStartupEnabled(value);
            Changed();
        }
    }

    public bool CtrlDragCreatesPad
    {
        get => S.CtrlDragCreatesPad;
        set
        {
            S.CtrlDragCreatesPad = value;
            CurrentApp.UpdateDesktopDrawing();
            Changed();
        }
    }

    public bool SnapEnabled
    {
        get => S.SnapEnabled;
        set { S.SnapEnabled = value; Changed(); }
    }

    public double IntelligentSpacing
    {
        get => S.IntelligentSpacing;
        set { S.IntelligentSpacing = Math.Round(value); Changed(); }
    }

    public string RollUpExpandMode
    {
        get => S.RollUpExpandMode;
        set { S.RollUpExpandMode = value; Changed(); }
    }

    // ===== Shortcuts =====

    public string ToggleHotkey
    {
        get => S.QuickHideHotkey;
        set { S.QuickHideHotkey = value; S.QuickHideEnabled = true; HotkeysChanged(); }
    }

    public string NewPadHotkey
    {
        get => S.NewPadHotkey;
        set { S.NewPadHotkey = value; HotkeysChanged(); }
    }

    public string RollUpAllHotkey
    {
        get => S.RollUpAllHotkey;
        set { S.RollUpAllHotkey = value; HotkeysChanged(); }
    }

    public string SettingsHotkey
    {
        get => S.SettingsHotkey;
        set { S.SettingsHotkey = value; HotkeysChanged(); }
    }

    /// <summary>Warning shown when a shortcut is already owned by another app.</summary>
    public string HotkeyStatus
    {
        get => _hotkeyStatus;
        private set { _hotkeyStatus = value; OnPropertyChanged(); }
    }

    // ===== Appearance: pad =====

    public string PadColor
    {
        get => A.PadColor;
        set { A.PadColor = value; AppearanceChanged(); }
    }

    public double PadOpacity
    {
        get => A.PadOpacity;
        set { A.PadOpacity = Math.Round(value, 2); AppearanceChanged(); }
    }

    // ===== Appearance: glass =====

    /// <summary>Glass options only matter when the default colour is glass, or a pad uses it.</summary>
    public bool IsGlass => PadMaterials.IsGlass(A.PadColor);

    public double GlassOptionsOpacity => IsGlass ? 1 : 0.6;

    public GlassBlur GlassBlur
    {
        get => A.GlassBlur;
        set { A.GlassBlur = value; AppearanceChanged(); }
    }

    public double GlassTint
    {
        get => A.GlassTint;
        set { A.GlassTint = Math.Round(value, 2); AppearanceChanged(); }
    }

    public double GlassShine
    {
        get => A.GlassShine;
        set { A.GlassShine = Math.Round(value, 2); AppearanceChanged(); }
    }

    public double CornerRadius
    {
        get => A.CornerRadius;
        set { A.CornerRadius = Math.Round(value); AppearanceChanged(); }
    }

    public string BorderColor
    {
        get => A.BorderColor;
        set { A.BorderColor = value; AppearanceChanged(); }
    }

    public double BorderThickness
    {
        get => A.BorderThickness;
        set { A.BorderThickness = Math.Clamp(Math.Round(value), 0, PadAppearance.MaxBorderThickness); AppearanceChanged(); }
    }

    // ===== Appearance: title bar =====

    public string TitleBarColor
    {
        get => A.TitleBarColor;
        set { A.TitleBarColor = value; AppearanceChanged(); }
    }

    public double TitleBarOpacity
    {
        get => A.TitleBarOpacity;
        set { A.TitleBarOpacity = Math.Round(value, 2); AppearanceChanged(); }
    }

    public string TitleTextColor
    {
        get => A.TitleTextColor;
        set { A.TitleTextColor = value; AppearanceChanged(); }
    }

    public double TitleFontSize
    {
        get => A.TitleFontSize;
        set { A.TitleFontSize = Math.Round(value); AppearanceChanged(); }
    }

    public TitleAlignment TitleAlignment
    {
        get => A.TitleAlignment;
        set { A.TitleAlignment = value; AppearanceChanged(); }
    }

    public string TitleFont
    {
        get => A.TitleFont;
        set { A.TitleFont = value; AppearanceChanged(); }
    }

    public bool TitleUppercase
    {
        get => A.TitleUppercase;
        set { A.TitleUppercase = value; AppearanceChanged(); }
    }

    // ===== Appearance: icons =====

    public double IconSize
    {
        get => A.IconSize;
        set { A.IconSize = value; AppearanceChanged(); }
    }

    public bool ShowIconLabels
    {
        get => A.ShowIconLabels;
        set { A.ShowIconLabels = value; AppearanceChanged(); }
    }

    /// <summary>Puts every appearance value back to its default (pad colours are kept).</summary>
    public void ResetAppearance()
    {
        S.Appearance = new PadAppearance();
        AppearanceChanged();
        OnPropertyChanged(string.Empty);
    }

    // ===== Live preview =====

    public Brush PreviewBackground => IsGlass
        ? PadVisuals.GlassFill(A.PadColor, A.GlassTint)
        : PadVisuals.Solid(A.PadColor, A.PadOpacity, Colors.Black);

    public Brush PreviewBorderBrush => IsGlass
        ? PadVisuals.GlassEdge(A.PadColor, A.GlassShine)
        : PadVisuals.Solid(A.BorderColor, 1, Colors.Transparent);

    public Thickness PreviewBorderThickness => new(IsGlass ? Math.Max(1, A.BorderThickness) : A.BorderThickness);

    public Brush PreviewSheen => IsGlass ? PadVisuals.GlassSheen(A.PadColor, A.GlassShine) : Brushes.Transparent;

    public Visibility PreviewSheenVisibility => IsGlass ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The preview fakes the desktop blur with a blurred copy of its own backdrop.</summary>
    public Visibility PreviewBlurVisibility => IsGlass && A.GlassBlur != GlassBlur.Off ? Visibility.Visible : Visibility.Collapsed;

    public double PreviewBlurRadius => A.GlassBlur == GlassBlur.Strong ? 28 : 12;
    public System.Windows.CornerRadius PreviewCornerRadius => new(A.CornerRadius);
    public Brush PreviewTitleBarBrush => PadVisuals.Solid(A.TitleBarColor, A.TitleBarOpacity, Colors.Black);

    public System.Windows.CornerRadius PreviewTitleBarCornerRadius
    {
        get
        {
            var inner = Math.Max(0, A.CornerRadius - A.BorderThickness);
            return new System.Windows.CornerRadius(inner, inner, 0, 0);
        }
    }

    public Brush PreviewTitleBrush => PadVisuals.Solid(A.TitleTextColor, 1, Colors.White);
    public FontFamily PreviewTitleFont => PadVisuals.Font(A.TitleFont);
    public HorizontalAlignment PreviewTitleAlignment => PadVisuals.Align(A.TitleAlignment);
    public string PreviewTitle => A.TitleUppercase ? "WORKSPACE" : "Workspace";
    public Brush PreviewLabelBrush => PadVisuals.LabelBrushFor(A.PadColor);
    public Visibility PreviewLabelVisibility => A.ShowIconLabels ? Visibility.Visible : Visibility.Collapsed;
    public double PreviewIconSize => A.IconSize;
    public double PreviewCellWidth => A.IconSize + 32;

    // ===== Plumbing =====

    /// <summary>Saves (debounced) and notifies the caller's binding.</summary>
    private void Changed([CallerMemberName] string? name = null)
    {
        ScheduleSave();
        OnPropertyChanged(name);
    }

    private void AppearanceChanged([CallerMemberName] string? name = null)
    {
        CurrentApp.Host?.ApplyAppearance();
        foreach (var property in PreviewProperties)
            OnPropertyChanged(property);

        Changed(name);
    }

    private void HotkeysChanged([CallerMemberName] string? name = null)
    {
        var failed = CurrentApp.RegisterHotkeys();
        HotkeyStatus = failed.Count == 0
            ? string.Empty
            : $"! In use by another app: {string.Join(", ", failed)}";

        Changed(name);
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes any pending change now — called on the timer and when the window closes.</summary>
    public void FlushSave()
    {
        if (!_saveTimer.IsEnabled)
            return;

        _saveTimer.Stop();
        _settings.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
