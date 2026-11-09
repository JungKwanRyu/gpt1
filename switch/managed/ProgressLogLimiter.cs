namespace WogSwitch;

internal sealed class ProgressLogLimiter
{
    private const string Prefix = "[wog] [form] 원본 게임 폴더에서 자료를 꺼내는 중";
    private readonly object gate = new();
    private long last = long.MinValue;

    internal bool Keep(string line, long elapsedMilliseconds)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        bool final = false;
        int slash = line.LastIndexOf('/'), space = line.LastIndexOf(' ');
        if (space >= 0 && slash > space && int.TryParse(line[(space + 1)..slash], out int done) &&
            int.TryParse(line[(slash + 1)..], out int total)) final = done == total;
        lock (gate)
        {
            if (last == long.MinValue || final || elapsedMilliseconds - last >= 500)
            { last = elapsedMilliseconds; return true; }
            return false;
        }
    }
}
