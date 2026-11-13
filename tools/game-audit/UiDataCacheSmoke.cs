using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WogSwitch;
internal static class UiDataCacheSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags f=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var data=new byte[]{1,2,3,4};AudioReuse.Put(data,"cached",4);if(!AudioReuse.Try<string>(data,out var reused)||reused!="cached"||AudioReuse.Try<string>(new byte[]{1,2,3,5},out _))throw new Exception("Audio identity changed");
        if(!AudioReuse.CopyRange(new List<short>{1,2,3,4},1,2).SequenceEqual(new short[]{2,3}))throw new Exception("PCM tail copy differs");
        var assets=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="WarOfGenesis.Assets");RuntimeHelpers.PrepareMethod(assets.GetType("WarOfGenesis.Assets.BinkAudio",true)!.GetMethod("Decode")!.MethodHandle);
        var text=new Dictionary<string,(uint[] Px,int W,int H)>();for(int i=0;i<500;i++)TextCache.Put(text,i.ToString(),(new uint[]{(uint)i},1,1));TextCache.Try(text,"0",out _);TextCache.Put(text,"new",(new uint[]{0},1,1));if(!text.ContainsKey("0")||text.ContainsKey("1")||text.Count!=500)throw new Exception("Text LRU eviction failed");text.Clear();TextCache.Put(text,"fresh",(new uint[]{0},1,1));if(text.Count!=1)throw new Exception("Text invalidation failed");
        var owner=new object();if(UiDataCache.SlotTry<string>(owner,"slot",out _))throw new Exception("Unexpected cache hit");
        UiDataCache.SlotPut<string?>(owner,"slot",null);if(!UiDataCache.SlotTry<string?>(owner,"slot",out var empty)||empty!=null)throw new Exception("Empty slots not cached");
        UiDataCache.Clear(owner);if(UiDataCache.SlotTry<string>(owner,"slot",out _))throw new Exception("Stale header after invalidation");
        var options=new JsonSerializerOptions{WriteIndented=true};var value=new {Name="한글",Items=new[]{1,2,3}};
        string compact=UiDataCache.Serialize(value,options), original=JsonSerializer.Serialize(value,options);
        if(compact.Length>=original.Length||!options.WriteIndented||JsonSerializer.Deserialize<JsonElement>(compact).GetProperty("Name").GetString()!="한글")throw new Exception("Save JSON semantics changed");
        var wt=game.GetType("DuelDx.GameWindow",true)!;var window=RuntimeHelpers.GetUninitializedObject(wt);
        wt.GetField("_fontScalePercent",f)!.SetValue(window,100);wt.GetField("_zoom",f)!.SetValue(window,1.0);
        var mt=game.GetType("DuelDx.MosesScene",true)!;var mail=RuntimeHelpers.GetUninitializedObject(mt);mt.GetFields(f).Single(x=>x.FieldType==wt).SetValue(mail,window);
        var lines=new List<string>{"캐시 검사"};UiDataCache.MailPut(mail,"body",292,12f,lines);
        if(!UiDataCache.MailTry<List<string>>(mail,"body",292,12f,out var found)||!ReferenceEquals(lines,found))throw new Exception("Mail cache missed");
        wt.GetField("_fontScalePercent",f)!.SetValue(window,125);if(UiDataCache.MailTry<List<string>>(mail,"body",292,12f,out _))throw new Exception("Font change reused stale wrapping");
        foreach(var pair in new[]{(mt,"WrapText"),(game.GetType("DuelDx.SlotsScreen",true)!,"SlotHead"),(game.GetType("DuelDx.SlotsScreen",true)!,"DrawSlots")})RuntimeHelpers.PrepareMethod(pair.Item1.GetMethod(pair.Item2,f)!.MethodHandle);
        Console.WriteLine("PASS mail cache context invalidation, empty slot reuse/invalidation, compact save JSON and actual cache wrappers JIT validation");
    }
}
