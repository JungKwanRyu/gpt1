using System.Threading;
namespace WogSwitch;

// Mixer blocks use a monotonically increasing 44100-Hz output-frame index.
// Source positions are mapped to the device's released-frame clock; UI never
// takes the audio-device lock. Each voice keeps eight recent 40ms segments.
public static class VoicePlayback
{
    private readonly record struct Segment(long Output,double Start,double End,double Rate);
    private sealed class Track { internal readonly Segment[] Parts=new Segment[8]; internal int Count,Next; internal bool Canceled; }
    private static readonly object gate=new();
    private static readonly Dictionary<int,Track> tracks=new();
    private static readonly Queue<int> order=new();
    [ThreadStatic] private static long block,opening;
    private static long played,origin;
    private static int epoch,rate=44100,active;
    public static void BeginBlock(long written) => block=written*1764;
    public static void Opening(long written) => opening=written*1764;
    internal static int StartOutput(int sampleRate)
    {
        lock(gate){origin=opening;rate=sampleRate;played=origin;active=1;return ++epoch;}
    }
    internal static void Presented(int id,long frames)
    { lock(gate)if(id==epoch&&active!=0)played=Math.Max(played,origin+frames*44100/Math.Max(1,rate)); }
    internal static void StopOutput(int id) { lock(gate)if(id==epoch)active=0; }
    public static void Record(int tag,double start,double end,double step)
    {
        if(tag<=0 || step<=0 || !double.IsFinite(end))return;
        lock(gate)
        {
            if(!tracks.TryGetValue(tag,out var t)){
                t=new Track();tracks[tag]=t;order.Enqueue(tag);
                while(order.Count>512)tracks.Remove(order.Dequeue());
            }
            int last=(t.Next+7)%8;
            if(t.Count>0&&start==0&&t.Parts[last].End>0&&t.Parts[last].Output!=block){t.Count=t.Next=0;}
            t.Canceled=false;last=(t.Next+7)%8;
            if(t.Count>0&&t.Parts[last].Output==block)t.Parts[last]=new(block,start,end,step*44100);
            else {t.Parts[t.Next]=new(block,start,end,step*44100);t.Next=(t.Next+1)%8;t.Count=Math.Min(8,t.Count+1);}
        }
    }
    public static void Cancel(int tag) { lock(gate)if(tracks.TryGetValue(tag,out var t))t.Canceled=true; }
    public static double Fraction(double fallback,int tag,double duration)
    { lock(gate)if(active!=0&&!tracks.ContainsKey(tag))return fallback>=1?1:0; double ratio=Ratio(tag,duration); return double.IsNaN(ratio)?fallback:ratio; }
    public static int Reveal(int tag,double duration,int characters)
    { double ratio=Ratio(tag,duration); return double.IsNaN(ratio)?-1:(int)Math.Clamp(ratio*characters,0,characters); }
    private static double Ratio(int tag,double duration)
    {
        lock(gate)
        {
            if(active==0)return double.NaN;
            if(!tracks.TryGetValue(tag,out var t)||t.Count==0)return 0;
            if(t.Canceled)return 1;
            double source=0;double hz=44100;
            for(int i=0;i<t.Count;i++){
                var p=t.Parts[(t.Next-t.Count+i+8)%8];hz=p.Rate;
                if(played<p.Output)break;
                double fraction=Math.Clamp((played-p.Output)/1764.0,0,1);
                source=p.Start+(p.End-p.Start)*fraction;
                if(fraction<1)break;
            }
            return Math.Clamp(source/Math.Max(1,hz)/Math.Max(.001,duration),0,1);
        }
    }
}
