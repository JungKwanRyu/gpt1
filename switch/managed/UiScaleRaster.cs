using System.Runtime.InteropServices;
namespace WogSwitch;
public static class UiScaleRaster
{
    public static bool TryUi(uint[] dst,int width,int height,uint[] src,int sw,int sh,int fx,int fy,int x,int y,double scale,int ax,int ay,int tx,int ty,double fade,(int Left,int Top,int Width,int Height)? clip)
    {
        if(!SwitchRaster.UseNative||!double.IsFinite(scale)||!double.IsFinite(fade))return false;
        if(scale<=.01)return true;
        int left=tx+(int)Math.Floor((x+fx-ax)*scale),top=ty+(int)Math.Floor((y+fy-ay)*scale);
        int dw=Math.Max(1,tx+(int)Math.Floor((x+fx+sw-ax)*scale)-left),dh=Math.Max(1,ty+(int)Math.Floor((y+fy+sh-ay)*scale)-top);
        return Copy(dst,width,height,src,sw,sh,left,top,dw,dh,Math.Clamp((int)(fade*256),0,256),-1,clip);
    }
    public static bool TryPortrait(uint[] dst,int width,int height,uint[] src,int sw,int sh,int x,int y,int w,int h,bool faded,bool tint,(int Left,int Top,int Width,int Height)? clip)
    {
        if(!SwitchRaster.UseNative||faded)return false;
        if(sw==0||sh==0||w<=0||h<=0)return true;
        double scale=Math.Min((double)w/sw,(double)h/sh);int dw=Math.Max(1,(int)(sw*scale)),dh=Math.Max(1,(int)(sh*scale));
        return Copy(dst,width,height,src,sw,sh,x+(w-dw)/2,y+(h-dh)/2,dw,dh,256,tint?10:-1,clip);
    }
    private static unsafe bool Copy(uint[] dst,int width,int height,uint[] src,int sw,int sh,int left,int top,int w,int h,int fade,int tint,(int Left,int Top,int Width,int Height)? clip)
    {
        if(width<=0||height<=0||sw<=0||sh<=0||(long)width*height>dst.Length||(long)sw*sh>src.Length||w<=0||h<=0)return false;
        int x0=Math.Max(0,left),y0=Math.Max(0,top),x1=(int)Math.Min(width,(long)left+w),y1=(int)Math.Min(height,(long)top+h);
        if(clip is {} c){x0=Math.Max(x0,c.Left);y0=Math.Max(y0,c.Top);x1=(int)Math.Min(x1,(long)c.Left+c.Width);y1=(int)Math.Min(y1,(long)c.Top+c.Height);}
        if(x0>=x1||y0>=y1)return true;
        fixed(uint* d=dst,s=src)wog_ui_scaled(d,width,s,sw,sh,left,top,w,h,x0,y0,x1,y1,fade,tint);
        return true;
    }
    [DllImport("WogSwitch",CallingConvention=CallingConvention.Cdecl)]
    private static extern unsafe void wog_ui_scaled(uint* dst,int dw,uint* src,int sw,int sh,int left,int top,int w,int h,int x0,int y0,int x1,int y1,int fade,int tint);
}
