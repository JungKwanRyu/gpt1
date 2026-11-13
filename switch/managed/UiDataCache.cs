using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
namespace WogSwitch;
public static class UiDataCache
{
    private sealed class State { internal readonly Dictionary<object, object?> Values = new(); }
    private static readonly ConditionalWeakTable<object, State> states = new();
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> options = new();
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static void Clear(object owner) => states.Remove(owner);
    private static object MailKey(object owner,string text,int width,float size)
    {
        var type=owner.GetType();var host=type.GetFields(Flags).Single(f=>f.FieldType.FullName=="DuelDx.GameWindow").GetValue(owner)!;
        var ht=host.GetType();
        return (text,width,size,(bool)type.GetField("_layerText",Flags)!.GetValue(owner)!,
            (int)ht.GetField("_fontScalePercent",Flags)!.GetValue(host)!, (double)ht.GetField("_zoom",Flags)!.GetValue(host)!);
    }
    public static bool MailTry<T>(object owner,string text,int width,float size,out T value)
    {
        if(states.GetOrCreateValue(owner).Values.TryGetValue(MailKey(owner,text,width,size),out var found)){value=(T)found!;return true;}
        value=default!;return false;
    }
    public static T MailPut<T>(object owner,string text,int width,float size,T value)
    {
        if(text.Length>65536)return value;
        var state=states.GetOrCreateValue(owner);if(state.Values.Count>=8)state.Values.Clear();
        state.Values[MailKey(owner,text,width,size)]=value;return value;
    }
    public static bool SlotTry<T>(object owner,string path,out T value)
    {
        if(states.GetOrCreateValue(owner).Values.TryGetValue(path,out var found)){value=(T)found!;return true;}
        value=default!;return false;
    }
    public static T SlotPut<T>(object owner,string path,T value)
    {
        var state=states.GetOrCreateValue(owner);if(state.Values.Count>=24)state.Values.Clear();state.Values[path]=value;return value;
    }
    public static string Serialize<T>(T state,JsonSerializerOptions source)
        => JsonSerializer.Serialize(state,options.GetValue(source,o=>new JsonSerializerOptions(o){WriteIndented=false}));
}
