using System.Reflection;
using System.Runtime.CompilerServices;
using WogSwitch;
internal static class PlaybackEffectSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var wt=game.GetType("DuelDx.GameWindow",true)!;var w=RuntimeHelpers.GetUninitializedObject(wt);
        void Set(string n,object v)=>wt.GetField(n,f)!.SetValue(w,v);
        Set("Cols",24);Set("Rows",50);Set("_zoom",1.0);Set("_resW",640);Set("_resH",480);
        Set("_fadeInStart",-1.0);Set("_fadeOutStart",-1.0);
        int W=(int)wt.GetProperty("BoardWidth",f)!.GetValue(w)!,H=(int)wt.GetProperty("BoardHeight",f)!.GetValue(w)!;
        int vw=(int)wt.GetProperty("ViewWidth",f)!.GetValue(w)!,vh=(int)wt.GetProperty("ViewHeight",f)!.GetValue(w)!;
        var fb=new uint[W*H];Set("_fb",fb);
        var initial=Enumerable.Range(0,fb.Length).Select(i=>unchecked((uint)(i*1664525+1013904223))).ToArray();
        var ft=game.GetType("DuelDx.FieldScene",true)!;var field=RuntimeHelpers.GetUninitializedObject(ft);
        ft.GetFields(f).Single(x=>x.FieldType==wt).SetValue(field,w);
        int cases=0;
        void Compare(object target,MethodInfo method,object[] args,Action<uint[]> reference)
        {
            initial.CopyTo(fb,0);var expected=(uint[])initial.Clone();reference(expected);
            method.Invoke(target,args);
            if(!fb.SequenceEqual(expected))throw new InvalidDataException("Effect execution mismatch: "+method.Name+" case="+cases);
            cases++;
        }
        SwitchRaster.UseNative=true;
        foreach(bool enabled in new[]{false,true}) {
            ft.GetField("_fieldGray",f)!.SetValue(field,enabled);
            Compare(field,ft.GetMethod("ApplyFieldGray",f)!,Array.Empty<object>(),px=>{
                if(!enabled)return;for(int y=0;y<vh;y++)for(int x=0;x<vw;x++){
                    uint c=px[y*W+x],g=(7*(c>>16&255)+2*(c>>8&255)+(c&255))/10;
                    px[y*W+x]=0xff000000|g<<16|g<<8|g;
                }
            });
        }
        foreach(int tick in new[]{0,1,8,16,30,31,40,61})foreach(bool back in new[]{false,true}) {
            Set("_lastTime",tick/30.0);uint color=0xff80d035;
            ft.GetField("_fieldFade",f)!.SetValue(field,(0.0,31,31,color,back));
            Compare(field,ft.GetMethod("DrawFieldFade",f)!,new object[]{0,0},px=>{
                int actualTick=(int)((tick/30.0)*30),level=actualTick<31?actualTick:Math.Min(63,31+actualTick-31);
                int power=level<32?level:back?63-level:31;
                if(power<=0)return;
                for(int y=0;y<480;y++)for(int x=0;x<640;x++){
                    uint c=px[y*W+x],outc=c&0xff000000;
                    for(int sh=0;sh<=16;sh+=8){int a=(int)(c>>sh&255),b=(int)(color>>sh&255);outc|=(uint)(a+(b-a)*power/31)<<sh;}
                    px[y*W+x]=outc;
                }
            });
        }
        ft.GetField("_fieldFade",f)!.SetValue(field,null);
        Compare(field,ft.GetMethod("DrawFieldFade",f)!,new object[]{0,0},_=>{});
        foreach(double time in new[]{0.0,0.1,0.3,0.6}) {
            Set("_fadeInStart",0.0);Set("_lastTime",time);
            Compare(w,wt.GetMethod("DrawSceneFade",f)!,Array.Empty<object>(),px=>{
                int k=Math.Clamp(2*(int)(time*30),0,31);if(k>=31)return;
                for(int y=0;y<vh;y++)for(int x=0;x<vw;x++){
                    uint c=px[y*W+x],outc=0xff000000;
                    for(int sh=0;sh<=16;sh+=8)outc|=(uint)((int)(c>>sh&255)*k/31)<<sh;
                    px[y*W+x]=outc;
                }
            });
        }
        var ct=game.GetType("DuelDx.CutscenePlayer",true)!;var cut=RuntimeHelpers.GetUninitializedObject(ct);
        ct.GetFields(f).Single(x=>x.FieldType==wt).SetValue(cut,w);
        foreach(var size in new[]{(vw,vh),(32,24)}) {
            uint[] frame=Enumerable.Range(0,size.Item1*size.Item2).Select(i=>(uint)i*12345).ToArray();
            ct.GetField("_frame",f)!.SetValue(cut,frame);ct.GetField("_frameW",f)!.SetValue(cut,size.Item1);ct.GetField("_frameH",f)!.SetValue(cut,size.Item2);
            Compare(cut,ct.GetMethod("PaintFrame",f)!,Array.Empty<object>(),px=>{
                int width=Math.Min(vw,vh*size.Item1/size.Item2),height=width*size.Item2/size.Item1;
                int left=(vw-width)/2,top=(vh-height)/2;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)px[(top+y)*W+left+x]=frame[y*size.Item2/height*size.Item1+x*size.Item1/width]|0xff000000;
            });
        }
        var over=Enumerable.Range(0,640*480).Select(i=>unchecked((uint)(i*1234567))).ToArray();
        var under=Enumerable.Range(0,640*480).Select(i=>unchecked((uint)(i*76543))).ToArray();
        var wipeType=ft.GetNestedType("FieldWipe",BindingFlags.Public|BindingFlags.NonPublic)!;
        foreach(var item in new[]{("DrawSlideWipe",901),("DrawCombWipe",903),("DrawStreakWipe",904),("DrawGatherWipe",907),("DrawLineWipe",908),("DrawDissolveWipe",909)})
        foreach(int tick in new[]{0,1,15,30,59})foreach(bool hasBase in new[]{false,true}) {
            int[] ways=item.Item2==907?new[]{1001,3004}:item.Item2==904?new[]{0,1,2,3}:item.Item2==903?new[]{4,7}:new[]{1};
            foreach(int way in ways) {
                object wipe=Activator.CreateInstance(wipeType,new object?[]{item.Item2,way,60,0.0,hasBase?under:null,over,8})!;
                var args=new object[]{0,0,wipe,tick};
                initial.CopyTo(fb,0);ft.GetMethod("SwitchOriginal"+item.Item1,f)!.Invoke(field,args);var expected=(uint[])fb.Clone();
                initial.CopyTo(fb,0);ft.GetMethod(item.Item1,f)!.Invoke(field,args);
                if(!fb.SequenceEqual(expected))throw new InvalidDataException("Wipe mismatch "+item+" tick="+tick+" way="+way);
                cases++;
            }
        }
        SwitchRaster.UseNative=false;
        Console.WriteLine("PASS actual patched gray/field fade/scene fade/movie paint methods execute with exact reference pixels: "+cases+" cases; native C + scaled fallback");
    }
}
