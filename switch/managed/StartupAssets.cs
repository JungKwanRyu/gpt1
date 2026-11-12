namespace WogSwitch;
internal static class StartupAssets
{
    internal static string Select(LauncherSettings settings,Action<string> log)
    {
        string Fallback(string reason){log("STARTUP asset reuse skipped: "+reason);return settings.GameAssets;}
        bool Complete(string path)=>File.Exists(Path.Combine(path,"data/TXR/Txr.dat"))&&Directory.Exists(Path.Combine(path,"characters"));
        if(Complete(settings.GameAssets)){log("STARTUP explicit assets already complete");return settings.GameAssets;}
        if(!settings.Part2Present)return Fallback("part2 originals absent; preserve part1 mode");
        string candidate=Path.Combine(settings.SaveDir,"assets"),shipped=Path.Combine(settings.ReleaseDir,"assets"),manifest=Path.Combine(shipped,"originals.txt");
        if(!Complete(candidate))return Fallback("critical extracted assets incomplete");
        if(!File.Exists(manifest))return Fallback("shipped manifest absent");
        try{
            string cached=Path.Combine(candidate,"originals.txt"),stamp=Path.Combine(candidate,".shipped");
            if(!File.Exists(cached)||!File.Exists(stamp))return Fallback("extraction manifest/stamp absent");
            if(GameLoader.Digest(cached)!=GameLoader.Digest(manifest))return Fallback("shipped manifest content changed");
            string[] fields=File.ReadAllText(stamp).Split('|');
            if(fields.Length!=3||fields[0].Length==0||!long.TryParse(fields[2],out _))return Fallback("invalid extraction stamp");
            int count=0;
            foreach(string raw in File.ReadLines(manifest)){
                string rel=raw.Trim();if(rel.Length==0||rel[0]=='#')continue;
                string full=Path.GetFullPath(rel.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar),candidate);
                if(!LauncherSettings.Contains(candidate,full)||!File.Exists(full))return Fallback("manifest file missing or unsafe: "+rel);count++;
            }
            if(count==0)return Fallback("empty extraction manifest");
            string ticks=File.GetLastWriteTimeUtc(manifest).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(fields[2]!=ticks){
                // ZIP extraction/SD copies can change timestamps. Reuse only if
                // every shipped input is byte-identical to its cached copy.
                foreach(string file in Directory.EnumerateFiles(shipped,"*",SearchOption.AllDirectories)){
                    string other=Path.Combine(candidate,Path.GetRelativePath(shipped,file));
                    if(!File.Exists(other)||new FileInfo(file).Length!=new FileInfo(other).Length||GameLoader.Digest(file)!=GameLoader.Digest(other))
                        return Fallback("timestamp changed and shipped content differs");
                }
                fields[1]=shipped;fields[2]=ticks;File.WriteAllText(stamp,string.Join('|',fields));
                log("STARTUP normalized timestamp-only asset stamp after shipped-content verification");
            }
            log("STARTUP reuse extracted assets; manifest files checked="+count);return candidate;
        }catch(IOException){return Fallback("cache I/O failure");}catch(UnauthorizedAccessException){return Fallback("cache access denied");}
    }
}
