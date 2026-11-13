using System.Runtime.CompilerServices;
namespace WogSwitch;
public static class TextCache
{
    private sealed class State {internal readonly LinkedList<string> Order=new();internal readonly Dictionary<string,LinkedListNode<string>> Nodes=new();internal long Bytes;}
    private static readonly ConditionalWeakTable<Dictionary<string,(uint[] Px,int W,int H)>,State> states=new();
    private static State Get(Dictionary<string,(uint[] Px,int W,int H)> cache){var state=states.GetOrCreateValue(cache);if(cache.Count==0){state.Order.Clear();state.Nodes.Clear();state.Bytes=0;}return state;}
    public static bool Try(Dictionary<string,(uint[] Px,int W,int H)> cache,string key,out (uint[] Px,int W,int H) value){var state=Get(cache);if(!cache.TryGetValue(key,out value))return false;if(state.Nodes.TryGetValue(key,out var node)){state.Order.Remove(node);state.Order.AddLast(node);}return true;}
    public static void Put(Dictionary<string,(uint[] Px,int W,int H)> cache,string key,(uint[] Px,int W,int H) value){var state=Get(cache);long size=4L*value.Px.Length;if(size>16*1024*1024)return;while(state.Order.Count>0&&(cache.Count>=500||state.Bytes+size>16*1024*1024)){string old=state.Order.First!.Value;state.Order.RemoveFirst();state.Nodes.Remove(old);if(cache.Remove(old,out var previous))state.Bytes-=4L*previous.Px.Length;}cache[key]=value;state.Bytes+=size;state.Nodes[key]=state.Order.AddLast(key);}
    public static void Keep(Dictionary<string,(uint[] Px,int W,int H)> cache) { }
}
