using System.Runtime.InteropServices;

namespace WogSwitch;

// Called by the separately loaded patched game assembly.
public static class SwitchRaster
{
    internal static bool UseNative { get; set; }

    public static unsafe bool TryFrame(uint[] destination, int width, int height, uint[] source, int sw, int sh,
        int left, int top, int mode, double fade, (int Left, int Top, int Width, int Height)? clip)
    {
        if (!UseNative || (uint)mode > 7 || !double.IsFinite(fade) || !Valid(destination, width, height, source, sw, sh)) return false;
        int x0 = Math.Max(0, left), y0 = Math.Max(0, top);
        int x1 = Math.Min(width, unchecked(left + sw)), y1 = Math.Min(height, unchecked(top + sh));
        if (clip is { } box)
        {
            x0 = Math.Max(x0, box.Left); y0 = Math.Max(y0, box.Top);
            x1 = Math.Min(x1, unchecked(box.Left + box.Width)); y1 = Math.Min(y1, unchecked(box.Top + box.Height));
        }
        if (x0 >= x1 || y0 >= y1) return true;
        int k = fade < 1 ? (fade <= 0 ? 0 : Math.Clamp((int)(fade * 256), 0, 256)) : 256;
        fixed (uint* dst = destination)
        fixed (uint* src = source) wog_draw_frame(dst, width, src, sw, left, top, x0, y0, x1, y1, mode, k);
        return true;
    }

    public static unsafe bool TryFill(uint[] destination, int width, int height, int x, int y, int w, int h, uint color)
    {
        if (!UseNative || width <= 0 || height <= 0 || (long)width * height > destination.Length) return false;
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(width, unchecked(x + w)), y1 = Math.Min(height, unchecked(y + h));
        if (x0 >= x1 || y0 >= y1) return true;
        fixed (uint* dst = destination) wog_fill_rect(dst, width, x0, y0, x1, y1, color);
        return true;
    }

    public static unsafe bool TryMasked(uint[] destination, int width, int height, uint[] source, int sw, int sh,
        int left, int top, double fade, bool additive)
    {
        if (!UseNative || !double.IsFinite(fade) || !Valid(destination, width, height, source, sw, sh)) return false;
        if (fade <= 0) return true;
        int x0 = Math.Max(0, left), y0 = Math.Max(0, top), x1 = Math.Min(width, unchecked(left + sw)), y1 = Math.Min(height, unchecked(top + sh));
        if (x0 >= x1 || y0 >= y1) return true;
        int k = fade < 1 ? Math.Clamp((int)(fade * 256), 0, 256) : 256;
        fixed (uint* dst = destination)
        fixed (uint* src = source) wog_blit_masked(dst, width, src, sw, left, top, x0, y0, x1, y1, k, additive ? 1 : 0);
        return true;
    }

    private static bool Valid(uint[] dst, int w, int h, uint[] src, int sw, int sh) =>
        w > 0 && h > 0 && sw > 0 && sh > 0 && (long)w * h <= dst.Length && (long)sw * sh <= src.Length;

    public static unsafe void BlitOpaque(uint[] destination, int width, int height, uint[]? source,
        int sourceWidth, int sourceHeight, int x, int y)
    {
        if (source == null) return;
        if (width <= 0 || height <= 0 || sourceWidth <= 0 || sourceHeight <= 0 ||
            (long)width * height > destination.Length || (long)sourceWidth * sourceHeight > source.Length)
            throw new ArgumentException("Invalid opaque backdrop dimensions");
        if (UseNative)
        {
            fixed (uint* dst = destination)
            fixed (uint* src = source)
                wog_blit_opaque(dst, width, height, src, sourceWidth, sourceHeight, x, y);
            return;
        }
        // PC preparation and the desktop smoke do not load a Horizon binary.
        for (int sy = 0; sy < sourceHeight; sy++)
            for (int sx = 0; sx < sourceWidth; sx++)
            {
                long dx = (long)x + sx, dy = (long)y + sy;
                if (dx >= 0 && dx < width && dy >= 0 && dy < height)
                    destination[(int)(dy * width + dx)] = source[sy * sourceWidth + sx] | 0xff000000u;
            }
    }

    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_blit_opaque(uint* dst, int dw, int dh, uint* src, int sw, int sh, int dx, int dy);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_draw_frame(uint* dst, int dw, uint* src, int sw, int left, int top, int x0, int y0, int x1, int y1, int mode, int fade);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_fill_rect(uint* dst, int dw, int x0, int y0, int x1, int y1, uint color);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_blit_masked(uint* dst, int dw, uint* src, int sw, int left, int top, int x0, int y0, int x1, int y1, int fade, int additive);
}
