namespace WogSwitch;

// Select an existing complete extraction without reopening/indexing the original
// archives. Validate the manifest every time so deleted files trigger repair.
internal static class StartupAssets
{
    internal static string Select(LauncherSettings settings, Action<string> log)
    {
        bool ExplicitComplete(string path) => File.Exists(Path.Combine(path,"data/TXR/Txr.dat")) && Directory.Exists(Path.Combine(path,"characters"));
        if(ExplicitComplete(settings.GameAssets)) return settings.GameAssets;
        string candidate=Path.Combine(settings.SaveDir,"assets");
        string manifest=Path.Combine(settings.ReleaseDir,"assets/originals.txt");
        if(!ExplicitComplete(candidate) || !File.Exists(manifest)) return settings.GameAssets;
        try
        {
            string cachedManifest=Path.Combine(candidate,"originals.txt"),stamp=Path.Combine(candidate,".shipped");
            if(!File.Exists(cachedManifest) || !File.Exists(stamp) || GameLoader.Digest(cachedManifest)!=GameLoader.Digest(manifest))return settings.GameAssets;
            string[] fields=File.ReadAllText(stamp).Split('|');
            if(fields.Length!=3 || fields[2]!=File.GetLastWriteTimeUtc(manifest).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture))return settings.GameAssets;
            int count=0;
            foreach(string raw in File.ReadLines(manifest))
            {
                string rel=raw.Trim();if(rel.Length==0 || rel[0]=='#')continue;
                string full=Path.GetFullPath(rel.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar),candidate);
                if(!LauncherSettings.Contains(candidate,full) || !File.Exists(full))return settings.GameAssets;
                count++;
            }
            if(count==0)return settings.GameAssets;
            log("STARTUP reuse extracted assets; manifest files checked="+count);
            return candidate;
        }
        catch(IOException){return settings.GameAssets;}
        catch(UnauthorizedAccessException){return settings.GameAssets;}
    }
}
