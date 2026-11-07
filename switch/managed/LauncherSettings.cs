namespace WogSwitch;

internal sealed class LauncherSettings
{
    internal string Root { get; }
    internal string ReleaseDir { get; }
    internal string OriginalPart2 { get; }
    internal string OriginalPart1 { get; }
    internal string GameAssets { get; }
    internal string CacheRoot => Path.Combine(Root, "patched-load");
    internal string ReportPath => Path.Combine(Root, "load-report.txt");
    internal string ShimPath => Path.Combine(Root, "managed", "WogShim.dll");
    internal string SaveDir => Path.Combine(Root, "save");

    private LauncherSettings(string root, Dictionary<string, string> values)
    {
        Root = Path.GetFullPath(NormalizeSdPath(root));
        string Resolve(string key, string fallback) => Path.GetFullPath(
            NormalizeSdPath(values.GetValueOrDefault(key, fallback)), Root);
        ReleaseDir = Resolve("release", "release");
        OriginalPart2 = Resolve("original", "original/part2");
        OriginalPart1 = Resolve("part1", "original/part1");
        GameAssets = Resolve("assets", "release/assets");
        foreach (string input in new[] { ReleaseDir, OriginalPart2, OriginalPart1, GameAssets })
            if (Contains(input, CacheRoot) || Contains(CacheRoot, input))
                throw new InvalidDataException("Patch output must be separate from input folders: " + input);
    }

    internal static LauncherSettings Read(string root)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string file = Path.Combine(NormalizeSdPath(root), "launcher.ini");
        if (File.Exists(file))
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line == "[game]") continue;
                int split = line.IndexOf('=');
                if (split <= 0) throw new InvalidDataException("Invalid launcher.ini line: " + line);
                string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
                if (!new[] { "release", "original", "part1", "assets" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unknown launcher.ini setting: " + key);
                if (value.Length == 0 || value.Contains('\0') || !values.TryAdd(key, value))
                    throw new InvalidDataException("Empty or repeated launcher.ini setting: " + key);
            }
        return new LauncherSettings(root, values);
    }

    // .NET's Unix assembly loader requires /absolute/paths. libnx accepts these
    // on the current SD device; native main explicitly selects sdmc:/ first.
    internal static string NormalizeSdPath(string path) =>
        path.StartsWith("sdmc:/", StringComparison.OrdinalIgnoreCase) ? path[5..] : path;

    internal static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSdPath(parent)));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSdPath(child)));
        string prefix = a.EndsWith(Path.DirectorySeparatorChar) ? a : a + Path.DirectorySeparatorChar;
        return a.Equals(b, comparison) || b.StartsWith(prefix, comparison);
    }

    internal bool HasAnyGameDll => WogPatcher.GamePatcher.GameAssemblies.Any(name => File.Exists(Path.Combine(ReleaseDir, name)));
    internal bool Part2Present => File.Exists(Path.Combine(OriginalPart2, "TXR", "Txr.dat")) &&
        Directory.Exists(Path.Combine(OriginalPart2, "Chr")) && Directory.Exists(Path.Combine(OriginalPart2, "Obs"));
    internal bool Part1Present => new[] { "Chars", "Obss", "Btls" }.All(name => Directory.Exists(Path.Combine(OriginalPart1, name)));
}
