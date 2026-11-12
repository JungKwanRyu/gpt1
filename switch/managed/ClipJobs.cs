using System.Runtime.CompilerServices;
namespace WogSwitch;
public static class ClipJobs
{
    private sealed class Jobs { internal readonly Dictionary<int,object> Items=new(); }
    private sealed class Entry<T> { internal readonly List<Action<T>> Callbacks=new(); }
    private static readonly ConditionalWeakTable<object,Jobs> jobs=new();
    public static Action<T> Subscribe<T>(object owner,int id,Action<T> callback,out bool first)
    {
        var set=jobs.GetValue(owner,_=>new Jobs());Entry<T> entry;
        lock(set){
            if(set.Items.TryGetValue(id,out var value)){((Entry<T>)value).Callbacks.Add(callback);first=false;return callback;}
            entry=new Entry<T>();entry.Callbacks.Add(callback);set.Items.Add(id,entry);first=true;
        }
        return value=>{
            Action<T>[] callbacks;
            lock(set){set.Items.Remove(id);callbacks=entry.Callbacks.ToArray();}
            foreach(var done in callbacks)done(value);
        };
    }
}
