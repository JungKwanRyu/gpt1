using WogSwitch;
internal static class StartupAssetsSmoke
{
    internal static void Run(string output)
    {
        string root=Path.Combine(output,"startup-assets-test");Directory.CreateDirectory(root);
        var settings=LauncherSettings.Read(root);string saved=Path.Combine(settings.SaveDir,"assets");
        string shipped=Path.Combine(settings.ReleaseDir,"assets");
        void Check(bool cache,string label) { if((StartupAssets.Select(settings,Console.WriteLine)==saved)!=cache)throw new InvalidDataException(label);Console.WriteLine("PASS "+label); }
        Check(false,"missing extracted cache uses ordinary preparation");
        Directory.CreateDirectory(Path.Combine(saved,"data/TXR"));Directory.CreateDirectory(Path.Combine(saved,"characters"));Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(saved,"data/TXR/Txr.dat"),"SYNTHETIC");
        string manifest=Path.Combine(shipped,"originals.txt");File.WriteAllText(manifest,"# synthetic manifest\ndata/TXR/Txr.dat\ncharacters/test.chr\n");
        PortableFiles.Copy(manifest,Path.Combine(saved,"originals.txt"),true);
        File.WriteAllText(Path.Combine(saved,".shipped"),"1.0.0|old-pc-path|"+File.GetLastWriteTimeUtc(manifest).Ticks);
        Check(false,"partial cache falls back even when Txr and characters exist");
        File.WriteAllText(Path.Combine(saved,"characters/test.chr"),"SYNTHETIC");
        Check(true,"complete extracted manifest reuses assets across SD path changes");
        File.Delete(Path.Combine(saved,"characters/test.chr"));Check(false,"deleted extracted asset triggers ordinary repair");
        File.WriteAllText(Path.Combine(saved,"characters/test.chr"),"SYNTHETIC");
        File.AppendAllText(manifest,"data/new.dat\n");Check(false,"changed shipped manifest invalidates extracted reuse");
        PortableFiles.Copy(manifest,Path.Combine(saved,"originals.txt"),true);File.WriteAllText(Path.Combine(saved,".shipped"),"bad");Check(false,"malformed shipped stamp falls back without modifying saves");
        Directory.Delete(root,true);
    }
}
