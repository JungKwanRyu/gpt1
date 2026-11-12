using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace WogSwitch;
public static class MosesRaster
{
    private sealed class FadeClock { internal int Expected;internal double Last,Credit;internal object? Picture;internal bool Active,Blue,Black; }
    private static readonly ConditionalWeakTable<object,FadeClock> clocks=new();
    public static int Step(object key,int remaining,double real,object? picture,bool blue,bool black)
    {
        var c=clocks.GetValue(key,_=>new FadeClock());
        if(remaining<=0){c.Active=false;return 0;}
        if(!c.Active||remaining!=c.Expected||c.Picture!=picture||c.Blue!=blue||c.Black!=black){c.Active=true;c.Last=real;c.Credit=0;}
        else {double dt=real-c.Last;if(dt>=0&&dt<=1)c.Credit+=dt*30;int ticks=(int)Math.Floor(c.Credit+1e-8);c.Credit-=ticks;remaining=Math.Max(0,remaining-ticks);}
        c.Last=real;c.Expected=remaining;c.Picture=picture;c.Blue=blue;c.Black=black;return remaining;
    }
    public static unsafe bool TryFade(object key,uint[] dst,int dw,int dh,int camY,int viewH,int ox,int oy,ref int remaining,bool blue,bool black,ref uint[]? from,double real)
    {
        if(!SwitchRaster.UseNative||dw<=0||dh<=0||(long)dw*dh>dst.Length||camY<0||viewH<0||(long)camY+viewH>dh||!double.IsFinite(real))return false;
        int fade=Step(key,remaining,real,from,blue,black);remaining=fade;
        if(fade<=0){from=null;return true;}
        int total=blue?30:15;if(fade>total)return false;
        bool cross=!blue&&!black&&from!=null&&from.Length==(long)viewH*dw;
        int mode=cross?0:blue?2:1,power=cross?256*fade/total:31*fade/total;
        int x0=blue?Math.Max(0,ox):0,x1=blue?Math.Min(dw,ox+640):dw,y0=blue?Math.Max(camY,oy):camY,y1=blue?Math.Min(camY+viewH,oy+480):camY+viewH;
        if(x0<x1&&y0<y1)fixed(uint* d=dst,s=from)wog_moses_fade(d,dw,s,camY,x0,y0,x1,y1,mode,power);
        return true;
    }
    [DllImport("WogSwitch",CallingConvention=CallingConvention.Cdecl)]
    private static extern unsafe void wog_moses_fade(uint* dst,int dw,uint* from,int camY,int x0,int y0,int x1,int y1,int mode,int power);
}
