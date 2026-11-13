using System.Security.Cryptography;
namespace WogSwitch;
public static class AudioReuse
{
    private static readonly object gate=new();
    private static readonly Dictionary<string,(object Value,long Bytes)> clips=new();
    private static long bytes;
    private const long Budget=64L*1024*1024;
    public static bool Try<T>(byte[] data,out T value){var key=Convert.ToHexString(SHA256.HashData(data));lock(gate){if(clips.TryGetValue(key,out var entry)){value=(T)entry.Value;return true;}}value=default!;return false;}
    public static T Put<T>(byte[] data,T value,int samples){long size=2L*samples;if(size>Budget||size<0)return value;var key=Convert.ToHexString(SHA256.HashData(data));lock(gate){if(clips.ContainsKey(key))return value;while(clips.Count>0&&(bytes+size>Budget||clips.Count>=8)){var first=clips.First();bytes-=first.Value.Bytes;clips.Remove(first.Key);}clips[key]=(value!,size);bytes+=size;}return value;}
    public static short[] CopyRange(List<short> source,int start,int count){var result=new short[count];source.CopyTo(start,result,0,count);return result;}
}
