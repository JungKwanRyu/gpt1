using System.Text;

namespace WogSwitch;

internal static class Diagnostics
{
    internal static string Stage { get; private set; } = "managed entry";

    internal static void Step(string stage, Action<string> log)
    {
        Stage = stage;
        log("STEP " + stage);
    }

    internal static string Message(Exception error)
    {
        try { return error.Message ?? "<no message>"; }
        catch { return "<message unavailable; exception resource lookup failed>"; }
    }

    // Never call Exception.ToString: the Switch BCL can itself throw while
    // looking up the localized inner-exception separator and hide the cause.
    internal static string Describe(Exception error)
    {
        var text = new StringBuilder();
        text.Append("Stage: ").Append(Stage).Append('\n');
        Exception? current = error;
        for (int depth = 0; current != null && depth < 12; depth++)
        {
            text.Append("Exception[").Append(depth).Append("]: ").Append(current.GetType().FullName)
                .Append("; HResult=0x").Append(current.HResult.ToString("X8")).Append('\n');
            text.Append("Message: ").Append(Message(current)).Append('\n');
            try
            {
                if (!string.IsNullOrEmpty(current.StackTrace)) text.Append(current.StackTrace).Append('\n');
            }
            catch { text.Append("<stack trace unavailable>\n"); }
            current = current.InnerException;
        }
        return text.ToString();
    }
}
