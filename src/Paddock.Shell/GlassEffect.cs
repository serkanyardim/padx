using System.Runtime.InteropServices;
using Paddock.Core.Models;

namespace Paddock.Shell;

/// <summary>
/// Real blur-behind for a window, via DWM's accent policy (the same mechanism
/// the Start menu and taskbar use). What shows through a pad is the wallpaper
/// and desktop, frosted. Unsupported systems simply get no blur.
/// </summary>
public static class GlassEffect
{
    private const int WCA_ACCENT_POLICY = 19;

    private enum AccentState
    {
        Disabled = 0,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    /// <summary>
    /// False when Windows' "Transparency effects" is off. DWM then refuses to
    /// blur anything and paints the accent as an opaque grey/black slab, so the
    /// blur must not be requested at all — the glass falls back to tint and sheen.
    /// </summary>
    public static bool IsBlurAvailable
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Sets the blur behind the window; null turns it off. The tint is left
    /// almost clear — the window's own content paints the glass colour, sheen
    /// and edge on top.
    /// </summary>
    public static void Apply(IntPtr hwnd, GlassBlur? blur)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var state = blur switch
        {
            GlassBlur.Strong => AccentState.AcrylicBlurBehind,
            GlassBlur.Light => AccentState.BlurBehind,
            _ => AccentState.Disabled,
        };

        var policy = new AccentPolicy
        {
            State = state,
            // Acrylic needs a non-zero alpha; keep it nearly transparent.
            GradientColor = state == AccentState.AcrylicBlurBehind ? 0x01000000u : 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = pointer,
                SizeOfData = size,
            };

            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// Clips the window (and so its blur) to a rounded rectangle, in physical
    /// pixels. A radius of zero, or <paramref name="enabled"/> false, removes the clip.
    /// </summary>
    public static void SetRoundedRegion(IntPtr hwnd, int width, int height, int radius, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (!enabled || radius <= 0 || width <= 0 || height <= 0)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        // The +1 makes the region cover the last row/column, as GDI regions exclude them.
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);

        // On success the system owns the region; only free it if the call failed.
        if (SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region);
    }
}
