using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Paddock.App.Views.Controls;

/// <summary>
/// Records a key combination ("Ctrl+Alt+N") when focused. Backspace or Delete
/// clears it (shortcut disabled); Escape leaves it unchanged. Global hotkeys
/// are suspended while it has focus, so pressing an existing shortcut records
/// it instead of triggering it.
/// </summary>
public class HotkeyBox : TextBox
{
    private const string NoneText = "none";

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).ShowHotkey()));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ShowHotkey();
    }

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private static App CurrentApp => (App)Application.Current;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        CurrentApp.SuspendHotkeys();
        Text = "press keys...";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowHotkey();
        CurrentApp.ResumeHotkeys();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                Keyboard.ClearFocus();
                return;
            case Key.Back:
            case Key.Delete:
                Hotkey = string.Empty;
                Keyboard.ClearFocus();
                return;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin:
                return;
        }

        var modifiers = Keyboard.Modifiers;
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;

        // A bare letter would fire every time it was typed anywhere.
        if (modifiers == ModifierKeys.None && !isFunctionKey)
            return;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());

        Hotkey = string.Join("+", parts);
        Keyboard.ClearFocus();
    }

    private void ShowHotkey()
        => Text = string.IsNullOrWhiteSpace(Hotkey) ? NoneText : Hotkey;
}
