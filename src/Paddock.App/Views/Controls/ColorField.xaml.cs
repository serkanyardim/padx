using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Paddock.App.Helpers;

namespace Paddock.App.Views.Controls;

/// <summary>
/// Colour input: a swatch that opens a palette, next to a hex field. Invalid
/// hex is rejected on commit and the field reverts to the current colour.
/// </summary>
public partial class ColorField : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata("#000000",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    private static readonly DependencyPropertyKey SwatchBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(SwatchBrush), typeof(Brush), typeof(ColorField), new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty SwatchBrushProperty = SwatchBrushKey.DependencyProperty;

    public ColorField()
    {
        InitializeComponent();
        Sync();
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Brush SwatchBrush => (Brush)GetValue(SwatchBrushProperty);

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorField)d).Sync();

    private void Sync()
    {
        HexBox.Text = Color;
        SetValue(SwatchBrushKey, PadVisuals.Solid(Color, 1, Colors.Transparent));
    }

    private void Commit()
    {
        var normalized = PadVisuals.Normalize(HexBox.Text);

        if (normalized is null)
            Sync();
        else
            Color = normalized;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e) => PalettePopup.IsOpen = true;

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
            Color = hex;

        PalettePopup.IsOpen = false;
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            HexBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Sync();
            e.Handled = true;
        }
    }

    private void HexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();
}
