using WogSwitch;

internal static class DiagnosticsSmoke
{
    internal static void Run()
    {
        var error = new BrokenFormatterException(new InvalidOperationException("ORIGINAL_CAUSE_RETAINED"));
        string text = Diagnostics.Describe(error);
        if (!text.Contains("ORIGINAL_CAUSE_RETAINED") || !text.Contains("BrokenFormatterException") || !text.Contains("InvalidOperationException"))
            throw new InvalidDataException("Original nested exception was lost");
        Console.WriteLine("PASS exception logging preserves the inner cause when ToString, Message and StackTrace throw");
    }

    private sealed class BrokenFormatterException(Exception inner) : Exception("unreadable", inner)
    {
        public override string Message => throw new NullReferenceException("simulated SR failure");
        public override string StackTrace => throw new NullReferenceException("simulated stack formatting failure");
        public override string ToString() => throw new NullReferenceException("simulated Exception.ToString failure");
    }
}
