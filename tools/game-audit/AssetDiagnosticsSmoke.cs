using System.Reflection;
using WogSwitch;

internal static class AssetDiagnosticsSmoke
{
    internal static void Run(string root, string output, LauncherSettings settings)
    {
        PlatformGameBridge.BeginAssetReport();
        var assets = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
        Type fileType = assets.GetType("WarOfGenesis.Assets.GameFiles", true)!;
        Type original = assets.GetType("WarOfGenesis.Assets.OriginalAssets", true)!;
        MethodInfo extract = original.GetMethod("Extract", BindingFlags.Static | BindingFlags.NonPublic)!;
        var patches = new Dictionary<string, (int Offset, byte Value)[]>();
        string fixture = Path.Combine(root, "synthetic-original");
        Directory.CreateDirectory(Path.Combine(fixture, "Dat"));
        object files = fileType.GetMethod("FromGameRoot")!.Invoke(null, new object[] { fixture })!;
        string to = Path.Combine(settings.SaveDir, "assets/data/Dat/missing.dat");
        if (!Equals(extract.Invoke(null, new[] { files, "data/Dat/missing.dat", to, patches }), false))
            throw new InvalidDataException("Missing source did not return false");
        extract.Invoke(null, new[] { files, "data/Dat/missing.dat", to, patches });
        byte[] index = new byte[42];
        BitConverter.GetBytes(0x10000u).CopyTo(index, 0);
        BitConverter.GetBytes(0u).CopyTo(index, 8); BitConverter.GetBytes(3u).CopyTo(index, 14);
        void Name(int at, int size, string name)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(name);
            for (int i = 0; i < size; i++) index[at + i] = (byte)((i < bytes.Length ? bytes[i] : 0) ^ 0xff);
        }
        Name(20, 9, "0001.dat"); Name(29, 13, "missing.pak");
        File.WriteAllBytes(Path.Combine(fixture, "Dat/Dat.idx"), index);
        files = fileType.GetMethod("FromGameRoot")!.Invoke(null, new object[] { fixture })!;
        if (!Equals(extract.Invoke(null, new[] { files, "data/Dat/0001.dat", to, patches }), false))
            throw new InvalidDataException("Missing PAK did not preserve original extraction failure behavior");
        string text = File.ReadAllText(Path.Combine(root, "asset-report.txt"));
        if (text.Split("NOT_FOUND_OR_UNMAPPED data/Dat/missing.dat", StringSplitOptions.None).Length != 2)
            throw new InvalidDataException("Repeated missing assets must not generate a report write every frame");
        if (!text.Contains("NOT_FOUND_OR_UNMAPPED data/Dat/missing.dat") || !text.Contains("IO_ERROR data/Dat/0001.dat") ||
            !text.Contains("FileNotFoundException") || !text.Contains("UNAVAILABLE_AFTER_IO_ERROR data/Dat/0001.dat"))
            throw new InvalidDataException("Asset diagnostics lost the missing-file/IO-error distinction");
        PortableFiles.Copy(Path.Combine(root, "asset-report.txt"), Path.Combine(output, "synthetic-asset-report.txt"), true);
        UnicodeCharacterPaths(root, settings, fixture, fileType, original, extract, patches);
        Console.WriteLine("PASS actual patched OriginalAssets.Extract distinguishes missing source from missing PAK IO failure without changing return behavior");
    }
    private static void UnicodeCharacterPaths(string root, LauncherSettings settings, string fixture, Type fileType,
        Type original, MethodInfo extract, Dictionary<string, (int Offset, byte Value)[]> patches)
    {
        string parent = Path.Combine(settings.SaveDir, "assets/characters");
        string nfc = "9921_한글", nfd = nfc.Normalize(System.Text.NormalizationForm.FormD);
        string existing = Path.Combine(parent, nfd); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "character.json"), "user-edited metadata");
        Directory.CreateDirectory(Path.Combine(fixture, "Chr"));
        byte[] expected = { 9, 8, 7, 6 };
        File.WriteAllBytes(Path.Combine(fixture, "Chr/9921.chr"), expected);
        PlatformGameBridge.BeginAssetPreparation();
        object files = fileType.GetMethod("FromGameRoot")!.Invoke(null, new object[] { fixture })!;
        string rel = "characters/" + nfc + "/9921.chr";
        string to = Path.Combine(settings.SaveDir, "assets", rel);
        if (!Equals(extract.Invoke(null, new[] { files, rel, to, patches }), true) ||
            !File.ReadAllBytes(Path.Combine(existing, "9921.chr")).SequenceEqual(expected) ||
            !PlatformGameBridge.PreparedAssetExists(to) ||
            File.ReadAllText(Path.Combine(existing, "character.json")) != "user-edited metadata")
            throw new InvalidDataException("NFC manifest did not extract to the existing NFD directory while preserving metadata");
        Type folder = original.Assembly.GetType("WarOfGenesis.Assets.AssetsFolder", true)!;
        folder.GetMethod("Use")!.Invoke(null, new object[] { Path.Combine(settings.SaveDir, "assets") });
        string found = (string)folder.GetMethod("Find")!.Invoke(null, new object[] { "characters/" + nfc })!;
        if (found != existing) throw new InvalidDataException("Patched AssetsFolder.Find lost the existing Unicode spelling");
        Directory.CreateDirectory(Path.Combine(parent, "9922_옛이름"));
        Directory.CreateDirectory(Path.Combine(parent, "9923_첫번째"));
        Directory.CreateDirectory(Path.Combine(parent, "9923_두번째"));
        PlatformGameBridge.BeginAssetPreparation();
        if (PlatformGameBridge.ResolveAssetPath(Path.Combine(parent, "9922_새이름/a.chr")) != Path.Combine(parent, "9922_옛이름/a.chr"))
            throw new InvalidDataException("Unique stable character ID did not resolve the existing transferred directory");
        bool rejected = false;
        try { PlatformGameBridge.ResolveAssetPath(Path.Combine(parent, "9923_모호함/a.chr")); }
        catch (IOException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Ambiguous character ID selected arbitrary metadata");
        Console.WriteLine("PASS actual patched extraction and AssetsFolder.Find reuse NFD/NFC character paths and preserve edited metadata; unique IDs resolve names; ambiguous IDs reject");
    }

}
