using System.Runtime.InteropServices;

namespace WogSwitch;

// Called by the separately loaded patched game assembly.
public static class SwitchRaster
{
    internal static bool UseNative { get; set; }

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
}
