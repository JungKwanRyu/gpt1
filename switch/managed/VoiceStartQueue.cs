using System.Collections.Concurrent;
namespace WogSwitch;
// A prepared dialogue begins after the next completed game presentation, so a
// cold portrait/font draw cannot play speech over an as-yet unseen dialog.
public static class VoiceStartQueue
{
    private static readonly ConcurrentQueue<(int Generation,Action Start)> pending=new();
    private static int generation;
    public static Action<T> Wrap<T>(Action<T> callback)
    {
        int session=Volatile.Read(ref generation);
        return value=>{
            if(session!=Volatile.Read(ref generation))return;
            if(pending.Count>=64){callback(default!);return;}
            pending.Enqueue((session,()=>callback(value)));
        };
    }
    internal static void Presented()
    {
        int count=Math.Min(64,pending.Count);
        while(count-->0&&pending.TryDequeue(out var work))if(work.Generation==Volatile.Read(ref generation))work.Start();
    }
    internal static void Reset(){Interlocked.Increment(ref generation);while(pending.TryDequeue(out _)){} }
}
