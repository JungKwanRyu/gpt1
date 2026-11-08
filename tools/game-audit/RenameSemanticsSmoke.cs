internal static class RenameSemanticsSmoke
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("WOG_TEST_LIBNX_RENAME") != "1") return;
        string destination = Path.Combine(output, "rename-semantics-fixture.dll");
        string source = destination + ".switch-temp";
        File.WriteAllText(destination, "existing patch");
        File.WriteAllText(source, "replacement patch");
        bool rejected = false;
        try { File.Move(source, destination, true); }
        catch (IOException) { rejected = true; }
        if (!rejected || !File.Exists(source) || File.ReadAllText(destination) != "existing patch")
            throw new InvalidDataException("The Horizon overwrite-rename failure was not reproduced");
        Console.WriteLine("PASS reproduced old File.Move(overwrite:true) failure with an existing patch destination");
    }
}
