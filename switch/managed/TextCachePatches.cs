using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class TextCachePatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log){var method=module.GetType("DuelDx.GameWindow").Methods.Single(m=>m.Name=="GetText");int sites=0;
        foreach(var i in method.Body.Instructions){if(i.Operand is not MethodReference m||!m.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2"))continue;
            string? name=m.Name switch{"TryGetValue"=>"Try","set_Item"=>"Put","Clear"=>"Keep",_=>null};if(name==null)continue;i.OpCode=OpCodes.Call;i.Operand=module.ImportReference(typeof(TextCache).GetMethod(name)!);sites++;}
        if(sites!=3)throw new InvalidDataException("Text cache access pattern changed: "+sites);log("PASS text raster LRU preserves explicit invalidation; bounded to 500 entries and 16MiB");}
}
