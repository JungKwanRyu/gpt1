using System.Runtime.InteropServices;

namespace WogSwitch;

// Called by the separately loaded patched game assembly.
public static class SwitchRaster
{
    internal static bool UseNative { get; set; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FieldPiece
    {
        internal uint* Pixels;
        internal int Pw, Ph, X, Y, W, H, Sx, Sy;
    }

    public static unsafe bool TryFieldBackdrop(uint[] destination, int width, int height,
        (uint[] Px, int W, int H)? background, (int X, int Y) camera,
        List<(int X1, int Y1, int W, int H, uint[] Px, int PicW, int PicH, int Vx, int Vy)> pieces,
        int ox, int oy, int flow)
    {
        if (!UseNative || background is not { } bg || !Valid(destination, width, height, bg.Px, bg.W, bg.H) || pieces.Count > 64) return false;
        int count = pieces.Count;
        for (int i = 0; i < count; i++)
        {
            var p = pieces[i];
            if (p.PicW <= 0 || p.PicH <= 0 || p.Px == null || (long)p.PicW * p.PicH > p.Px.Length) return false;
        }
        FieldPiece* descriptors = stackalloc FieldPiece[count];
        Span<GCHandle> handles = stackalloc GCHandle[count];
        int pinned = 0;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var p = pieces[i];
                handles[i] = GCHandle.Alloc(p.Px, GCHandleType.Pinned); pinned++;
                descriptors[i] = new FieldPiece {
                    Pixels = (uint*)handles[i].AddrOfPinnedObject(), Pw = p.PicW, Ph = p.PicH,
                    X = p.X1, Y = p.Y1, W = p.W, H = p.H,
                    Sx = unchecked((bg.W > 640 ? (p.PicW - p.W) * camera.X / (bg.W - 640) : 0) - p.Vx * flow),
                    Sy = unchecked((bg.H > 480 ? (p.PicH - p.H) * camera.Y / (bg.H - 480) : 0) - p.Vy * flow) };
            }
            fixed (uint* dst = destination)
            fixed (uint* src = bg.Px)
                wog_field_backdrop(dst, width, height, src, bg.W, bg.H, camera.X, camera.Y, ox, oy, descriptors, count);
            return true;
        }
        finally { for (int i = 0; i < pinned; i++) handles[i].Free(); }
    }

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

    public static bool TryVideoCopy(uint[] dst, int dw, int dh, uint[] src, int sw, int sh, int cx, int cy, int vw, int vh)
    {
        if (!UseNative || sw != vw || sh != vh || !Valid(dst, dw, dh, src, sw, sh)) return false;
        BlitOpaque(dst, dw, dh, src, sw, sh, cx, cy);
        return true;
    }

    public static unsafe void Transform(uint[] dst, int dw, int dh, int x, int y, int w, int h, int mode, uint color, int power)
    {
        if (dw <= 0 || dh <= 0 || (long)dw * dh > dst.Length || mode < 0 || mode > 2 || power < 0 || power > 31) throw new ArgumentException("Invalid transform");
        int x0=Math.Max(0,x), y0=Math.Max(0,y), x1=(int)Math.Min(dw,(long)x+w), y1=(int)Math.Min(dh,(long)y+h);
        if(x0>=x1 || y0>=y1) return;
        if(UseNative) { fixed(uint* p=dst) wog_transform(p,dw,x0,y0,x1,y1,mode,color,power); return; }
        for(int yy=y0;yy<y1;yy++) for(int xx=x0;xx<x1;xx++) {
            int i=yy*dw+xx; uint c=dst[i], result;
            if(mode==0) { uint g=(7*((c>>16)&255)+2*((c>>8)&255)+(c&255))/10; result=0xff000000|g<<16|g<<8|g; }
            else { result=mode==1?0xff000000:c&0xff000000; for(int shift=0;shift<=16;shift+=8) { int v=(int)(c>>shift&255); int n=mode==1?v*power/31:v+((int)(color>>shift&255)-v)*power/31; result|=(uint)n<<shift; } }
            dst[i]=result;
        }
    }
    [DllImport("WogSwitch", CallingConvention=CallingConvention.Cdecl)]
    private static extern unsafe void wog_transform(uint* dst,int dw,int x0,int y0,int x1,int y1,int mode,uint color,int power);

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
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void wog_field_backdrop(uint* dst, int dw, int dh, uint* bg, int bw, int bh, int cx, int cy, int ox, int oy, FieldPiece* pieces, int count);
}
