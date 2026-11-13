using System.Runtime.InteropServices;

namespace WogSwitch;

// Native counterparts preserve BlitMasked's tint -> status -> fade -> alpha order.
public static class BattleRaster
{
    public static unsafe bool TryMasked(uint[] destination, int width, int height, uint[] source, int sw, int sh,
        int left, int top, (int Mode, int Strength)? tint, (byte[] R, byte[] G, byte[] B)? status,
        double fade, bool additive)
    {
        if (!SwitchRaster.UseNative || !double.IsFinite(fade) || width <= 0 || height <= 0 || sw <= 0 || sh <= 0 ||
            (long)width * height > destination.Length || (long)sw * sh > source.Length) return false;
        if (fade <= 0) return true;
        if (status is { } st && (st.R == null || st.G == null || st.B == null || st.R.Length < 32 || st.G.Length < 32 || st.B.Length < 32)) return false;
        int x0 = Math.Max(0, left), y0 = Math.Max(0, top);
        int x1 = Math.Min(width, unchecked(left + sw)), y1 = Math.Min(height, unchecked(top + sh));
        if (x0 >= x1 || y0 >= y1) return true;
        int k = fade < 1 ? Math.Clamp((int)(fade * 256), 0, 256) : 256;
        int n = tint is { Mode: >= 1 and <= 7 } t ? 31 - 4 * t.Mode : 31;
        int level = tint is { } tt ? Math.Clamp(tt.Strength, 0, 31) * 255 / 31 : 0;
        // Part1Game.ScreenTint is verified by the patcher against the pinned game.
        int screen = tint is { Mode: 14 } ? 1 : 0;
        fixed (uint* dst = destination)
        fixed (uint* src = source)
        fixed (byte* r = status?.R)
        fixed (byte* g = status?.G)
        fixed (byte* b = status?.B)
            wog_battle_masked(dst, width, src, sw, left, top, x0, y0, x1, y1, k, additive ? 1 : 0, n, level, screen, r, g, b);
        return true;
    }

    public static unsafe bool TryFlash(uint[] pixels, (double At, int Mode, int Level, int Ticks) tint,
        double critAt, double now, bool part1)
    {
        if (!SwitchRaster.UseNative) return false;
        bool hasTint = tint.At >= 0 && now - tint.At <= tint.Ticks / 30.0 && tint.Mode is >= 1 and <= 7;
        // Keep the original negative/NaN comparison behavior and exact channel arithmetic.
        int crit = critAt < 0 || now - critAt > 1.0 / 30.0 ? 0 : part1 ? 1 : 2;
        if (!hasTint && crit == 0) return true;
        uint k = hasTint ? (uint)(31 - 4 * tint.Mode) : 31;
        uint add = hasTint ? unchecked((31 - k) * (uint)(unchecked(tint.Level * 255) / 31)) : 0;
        fixed (uint* dst = pixels) wog_battle_flash(dst, pixels.Length, k, add, hasTint ? 1 : 0, crit);
        return true;
    }

    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_battle_masked(uint* dst, int dw, uint* src, int sw, int left, int top,
        int x0, int y0, int x1, int y1, int fade, int additive, int n, int level, int screen, byte* r, byte* g, byte* b);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_battle_flash(uint* dst, int length, uint k, uint add, int tint, int crit);
}
