using System.Runtime.InteropServices;
namespace WogSwitch;
public static class NativeAudioDecoder
{
    public static unsafe bool TryRdft(float[] values, double[] real, double[] imaginary, int[] reverse, double[] cosine, double[] sine, int n)
    {
        if (!SwitchRaster.UseNative || n<2 || n>16384 || (n&(n-1))!=0 || values.Length<n || real.Length<n || imaginary.Length<n || reverse.Length<n || cosine.Length<n/2 || sine.Length<n/2) return false;
        fixed(float* x=values) fixed(double* re=real, im=imaginary, co=cosine, si=sine) fixed(int* rev=reverse)
            return wog_audio_rdft(x,re,im,rev,co,si,n)!=0;
    }
    [DllImport("WogSwitch",CallingConvention=CallingConvention.Cdecl)]
    private static extern unsafe int wog_audio_rdft(float* x,double* re,double* im,int* reverse,double* cosine,double* sine,int n);
}
