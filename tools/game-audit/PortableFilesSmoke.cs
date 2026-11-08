using WogSwitch;

internal static class PortableFilesSmoke
{
    internal static void Run(string output)
    {
        string root = Path.Combine(output, "portable-files"); Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.bin"), target = Path.Combine(root, "target.bin");
        byte[] data = Enumerable.Range(0, 170003).Select(i => (byte)(i * 31)).ToArray();
        File.WriteAllBytes(source, data); File.WriteAllText(target, "previous");
        if (Environment.GetEnvironmentVariable("WOG_TEST_LIBNX_RENAME") == "1")
        {
            bool failed = false;
            try { File.Copy(source, target, true); } catch (IOException) { failed = true; }
            if (!failed) throw new InvalidDataException("Unsupported native copy metadata fault was not reproduced");
            Console.WriteLine("PASS reproduced native File.Copy failure with unsupported fchmod");
        }
        PortableFiles.Copy(source, target, true);
        if (!File.ReadAllBytes(target).SequenceEqual(data) || !File.ReadAllBytes(source).SequenceEqual(data)) throw new InvalidDataException("Portable copy corrupted data");
        ExpectFailure(() => PortableFiles.Copy(source, target, false));
        ExpectFailure(() => PortableFiles.Copy(source, source, true));
        if (!File.ReadAllBytes(source).SequenceEqual(data) || !File.ReadAllBytes(target).SequenceEqual(data)) throw new InvalidDataException("Rejected copy changed files");
        string move = Path.Combine(root, "move.bin"); File.WriteAllText(move, "replacement");
        ExpectFailure(() => PortableFiles.Move(move, target, false));
        PortableFiles.Move(move, target, true);
        if (File.Exists(move) || File.ReadAllText(target) != "replacement") throw new InvalidDataException("Portable move failed");
        PortableFiles.Move(target, move, false);
        if (File.Exists(target) || File.ReadAllText(move) != "replacement") throw new InvalidDataException("Move to absent path failed");
        if (Environment.GetEnvironmentVariable("WOG_TEST_LIBNX_RENAME") == "1")
        {
            string failing = Path.Combine(root, "source.wog-fail-move"); File.WriteAllText(failing, "new");
            ExpectFailure(() => PortableFiles.Move(failing, move, true));
            if (File.ReadAllText(move) != "replacement" || File.ReadAllText(failing) != "new" || Directory.GetFiles(root, "*.wog-backup-*").Length != 0)
                throw new InvalidDataException("Move rollback did not restore original files");
        }
        Console.WriteLine("PASS portable copy byte integrity, overwrite refusal, same path rejection, move replacement, absent destination and failure rollback");
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new InvalidDataException("Expected file operation to fail");
    }
}
