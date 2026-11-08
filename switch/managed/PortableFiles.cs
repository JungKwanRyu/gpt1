namespace WogSwitch;

// Use only read/write/delete and rename to an absent path. The Switch SDK's
// native CopyFile requires unsupported file metadata operations; rename cannot
// replace a destination on the SD filesystem.
internal static class PortableFiles
{
    private static readonly object Gate = new();
    private static int serial;

    private static string Full(string path) => Path.GetFullPath(LauncherSettings.NormalizeSdPath(path));

    internal static void Copy(string source, string destination, bool overwrite)
    {
        source = Full(source); destination = Full(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source and destination are the same file: " + source);
        lock (Gate)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[64 * 1024];
            int count;
            // Avoid FileStream.CopyTo as well as File.Copy: a stream
            // implementation may select an unsupported native copy shortcut.
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) output.Write(buffer, 0, count);
        }
    }

    internal static void Move(string source, string destination, bool overwrite)
    {
        source = Full(source); destination = Full(destination);
        lock (Gate)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Move source is missing", source);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(destination)) throw new IOException("Move destination is a directory: " + destination);
            string? backup = null;
            if (File.Exists(destination))
            {
                if (!overwrite) throw new IOException("Move destination exists: " + destination);
                backup = destination + ".wog-backup-" + DateTime.UtcNow.Ticks.ToString("x") + "-" + Interlocked.Increment(ref serial);
                RenameToAbsent(destination, backup);
            }
            try { RenameToAbsent(source, destination); }
            catch (Exception error)
            {
                if (backup != null)
                {
                    if (!File.Exists(destination))
                    {
                        try { RenameToAbsent(backup, destination); }
                        catch (Exception rollback) { throw new IOException("Move failed; previous file retained at " + backup, new AggregateException(error, rollback)); }
                    }
                    else throw new IOException("Move failed; previous file retained at " + backup, error);
                }
                throw;
            }
            if (backup != null)
            {
                try { File.Delete(backup); }
                catch (IOException) { WogShim.Host.Log("Completed move; previous-file backup retained: " + backup); }
            }
        }
    }

    private static void RenameToAbsent(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Rename destination must be absent: " + destination);
        // The three-argument overload with overwrite=true selects rename on
        // Unix BCL9. The false/two-argument route can require unsupported links.
        // The destination is absent and all our copy/move calls hold Gate.
        File.Move(source, destination, true);
    }
}
