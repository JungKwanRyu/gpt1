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
        Console.WriteLine("PASS actual patched OriginalAssets.Extract distinguishes missing source from missing PAK IO failure without changing return behavior");
    }
}
