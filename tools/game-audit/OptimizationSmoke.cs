using System.Reflection;
using System.Runtime.CompilerServices;
using WogSwitch;
internal static class OptimizationSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags f=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var assets=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="WarOfGenesis.Assets");
        var fft=assets.GetType("WarOfGenesis.Assets.BinkAudio",true)!.GetNestedType("Fft",BindingFlags.NonPublic)!;
        int fftCases=0;
        foreach(int n in new[]{128,256,512,1024,2048,4096})foreach(int seed in new[]{1,17,4321}){
            var random=new Random(seed);float[] values=Enumerable.Range(0,n).Select(_=>(float)(random.NextDouble()*200-100)).ToArray();
            var expected=(float[])values.Clone();object a=fft.GetMethod("Get",f)!.Invoke(null,new object[]{n})!,b=fft.GetMethod("Get",f)!.Invoke(null,new object[]{n})!;
            SwitchRaster.UseNative=false;fft.GetMethod("InverseRdft",f)!.Invoke(a,new object[]{expected});
            SwitchRaster.UseNative=true;fft.GetMethod("InverseRdft",f)!.Invoke(b,new object[]{values});
            if(!values.Select(BitConverter.SingleToInt32Bits).SequenceEqual(expected.Select(BitConverter.SingleToInt32Bits)))throw new InvalidDataException("Native Bink RDFT differs from actual original: n="+n);fftCases++;
        }
        VoicePlayback.Opening(0);int clock=VoicePlayback.StartOutput(44100);VoicePlayback.BeginBlock(0);VoicePlayback.Record(700001,0,1764,1);
        if(VoicePlayback.Reveal(700001,1,100)!=0)throw new InvalidDataException("Text ran ahead of unreleased audio");
        VoicePlayback.Presented(clock,882);
        if(VoicePlayback.Reveal(700001,1,100)!=2)throw new InvalidDataException("Text did not follow released audio frame position");
        VoicePlayback.BeginBlock(1);VoicePlayback.Record(700001,1764,3528,1);VoicePlayback.Presented(clock,2646);
        if(VoicePlayback.Reveal(700001,1,100)!=6)throw new InvalidDataException("Dialogue clock used UI refresh time instead of PCM position");
        VoicePlayback.Opening(10);int reopened=VoicePlayback.StartOutput(44100);VoicePlayback.BeginBlock(10);VoicePlayback.Record(700002,0,1764,1);
        VoicePlayback.Presented(clock,999999);if(VoicePlayback.Reveal(700002,1,100)!=0)throw new InvalidDataException("Stale device epoch advanced a new voice");
        VoicePlayback.StopOutput(reopened);if(VoicePlayback.Fraction(.25,700002,1)!=.25)throw new InvalidDataException("No-output timing fallback changed");
        var owner=new object();int callbacks=0;var complete=ClipJobs.Subscribe<int>(owner,4,_=>callbacks++,out bool first);
        ClipJobs.Subscribe<int>(owner,4,_=>callbacks++,out bool second);complete(7);
        if(!first||second||callbacks!=2)throw new InvalidDataException("Duplicate voice jobs were not coalesced");
        var wt=game.GetType("DuelDx.GameWindow",true)!;var w=RuntimeHelpers.GetUninitializedObject(wt);
        void Set(string n,object v)=>wt.GetField(n,f)!.SetValue(w,v);
        Set("Cols",24);Set("Rows",50);Set("_zoom",1.0);Set("_resW",640);Set("_resH",480);Set("_realTime",0.0);
        int width=(int)wt.GetProperty("BoardWidth",f)!.GetValue(w)!,height=(int)wt.GetProperty("BoardHeight",f)!.GetValue(w)!,vh=(int)wt.GetProperty("ViewHeight",f)!.GetValue(w)!;
        uint[] initial=Enumerable.Range(0,width*height).Select(i=>unchecked((uint)(i*1664525+1013904223))).ToArray();var fb=(uint[])initial.Clone();Set("_fb",fb);
        var mt=game.GetType("DuelDx.MosesScene",true)!;var pixels=Enumerable.Range(0,width*vh).Select(i=>unchecked((uint)(i*321123+76543))).ToArray();int fadeCases=0;
        foreach(var mode in new[]{(false,false),(false,true),(true,false)})foreach(int phase in new[]{1,4,9,15}){
            object New(){var m=RuntimeHelpers.GetUninitializedObject(mt);mt.GetFields(f).Single(x=>x.FieldType==wt).SetValue(m,w);mt.GetField("_mosesFade",f)!.SetValue(m,phase);mt.GetField("_mosesBlueFade",f)!.SetValue(m,mode.Item1);mt.GetField("_mosesBlackFade",f)!.SetValue(m,mode.Item2);mt.GetField("_mosesFadeFrom",f)!.SetValue(m,(uint[])pixels.Clone());return m;}
            initial.CopyTo(fb,0);mt.GetMethod("SwitchOriginalMosesFade",f)!.Invoke(New(),null);var expected=(uint[])fb.Clone();
            initial.CopyTo(fb,0);SwitchRaster.UseNative=true;mt.GetMethod("SwitchMosesFade",f)!.Invoke(New(),null);
            if(!fb.SequenceEqual(expected))throw new InvalidDataException("Native Moses transition differs from actual original: "+mode+" phase="+phase);fadeCases++;
        }
        var key=new object();int remain=MosesRaster.Step(key,15,0,null,false,true);remain=MosesRaster.Step(key,remain,.4,null,false,true);
        if(remain!=3||MosesRaster.Step(key,remain,.5,null,false,true)!=0)throw new InvalidDataException("Moses fade duration depends on rendered frame count");
        var paused=new object();remain=MosesRaster.Step(paused,15,0,null,false,true);if(MosesRaster.Step(paused,remain,100,null,false,true)!=15)throw new InvalidDataException("Moses transition jumped after suspend");
        var talkType=game.GetType("DuelDx.TalkBox",true)!;var talk=RuntimeHelpers.GetUninitializedObject(talkType);talkType.GetFields(f).Single(x=>x.FieldType==wt).SetValue(talk,w);
        var blit=wt.GetMethod("BlitScaled",f)!;var faceType=blit.GetParameters()[0].ParameterType;
        uint[] face=Enumerable.Range(0,23*19).Select(i=>(unchecked((uint)(i*1664525+1111111))&0x00ffffffu)|(new uint[]{0,1,127,255}[i%4]<<24)).ToArray();
        object frame=Activator.CreateInstance(faceType,f,null,new object[]{face,23,19,-2,3},null)!;int scaleCases=0;
        foreach(var pt in new[]{(0,0),(-9,-7),(width-8,height-10)})foreach(bool tinted in new[]{false,true})foreach(object? clip in new object?[]{null,(2,4,17,13)}){
            Set("_uiClip",clip!);var args=new object[]{frame,pt.Item1,pt.Item2,60,tinted};
            initial.CopyTo(fb,0);SwitchRaster.UseNative=false;talkType.GetMethod("DrawTalkFace",f)!.Invoke(talk,args);var expected=(uint[])fb.Clone();
            initial.CopyTo(fb,0);SwitchRaster.UseNative=true;talkType.GetMethod("DrawTalkFace",f)!.Invoke(talk,args);if(!fb.SequenceEqual(expected))throw new InvalidDataException("Native tinted talk portrait mismatch");scaleCases++;
            foreach(bool faded in new[]{false,true}){
                var global=new object[]{frame,pt.Item1,pt.Item2,47,52,faded};initial.CopyTo(fb,0);SwitchRaster.UseNative=false;blit.Invoke(w,global);expected=(uint[])fb.Clone();
                initial.CopyTo(fb,0);SwitchRaster.UseNative=true;blit.Invoke(w,global);if(!fb.SequenceEqual(expected))throw new InvalidDataException("Native/fallback global portrait mismatch");scaleCases++;
            }
        }
        // Exercise real UI scale arithmetic through a synthetic sprite registered in the game cache.
        var sprite=game.GetType("DuelDx.UiSprite",true)!;var obj=RuntimeHelpers.GetUninitializedObject(sprite);
        var framesField=sprite.GetField("_frames",f)!;var frames=Activator.CreateInstance(framesField.FieldType)!;
        framesField.FieldType.GetMethod("Add")!.Invoke(frames,new object[]{(0,0),frame});framesField.SetValue(obj,frames);
        var cache=wt.GetFields(f).FirstOrDefault(x=>x.FieldType.IsGenericType&&x.FieldType.GetGenericTypeDefinition()==typeof(Dictionary<,>)&&x.FieldType.GetGenericArguments()[0]==typeof(int)&&x.FieldType.GetGenericArguments()[1]==sprite);
        if(cache!=null){var values=Activator.CreateInstance(cache.FieldType)!;cache.FieldType.GetMethod("Add")!.Invoke(values,new object[]{700003,obj});cache.SetValue(w,values);}
        Set("_uiClip",null!);
        if(cache!=null)foreach(double scale in new[]{.2,.7,1.0,1.8})foreach(double fade in new[]{0.0,.25,1.0}){
            var args=new object[]{700003,0,11,17,scale,0,0,0,0,fade,0,true};initial.CopyTo(fb,0);SwitchRaster.UseNative=false;talkType.GetMethod("DrawUiScaled",f)!.Invoke(talk,args);var expected=(uint[])fb.Clone();
            initial.CopyTo(fb,0);SwitchRaster.UseNative=true;talkType.GetMethod("DrawUiScaled",f)!.Invoke(talk,args);if(!fb.SequenceEqual(expected))throw new InvalidDataException("Native scaled UI arithmetic differs");scaleCases++;
        }
        foreach(var pair in new[]{(mt,"DrawMoses"),(mt,"SwitchMosesFade"),(talkType,"OpenTalkWindow"),(wt,"LoadClip")})RuntimeHelpers.PrepareMethod(pair.Item1.GetMethod(pair.Item2,f)!.MethodHandle);
        SwitchRaster.UseNative=false;
        Console.WriteLine("PASS native Bink RDFT bit-exact against actual decoder: "+fftCases+" cases; device PCM dialogue clock, reopen/mute and coalesced voice jobs");
        Console.WriteLine("PASS native Moses fade matches extracted original: "+fadeCases+" cases; real-time transition and suspend behavior");
        Console.WriteLine("PASS native scaled UI/portrait pixels and faded fallback: "+scaleCases+" cases; actual new methods pass runtime JIT validation");
    }
}
