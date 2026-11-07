using SkiaSharp;

namespace WogSwitch;

internal static class DialogPanel
{
    internal static SKBitmap Create(string fontFile, string caption, string text, string[] buttons, int selected)
    {
        var bitmap = new SKBitmap(new SKImageInfo(1280, 720, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            using var face = SKTypeface.FromFile(fontFile) ?? throw new InvalidDataException("Dialog font unavailable");
            using var title = new SKFont(face, 34);
            using var body = new SKFont(face, 24);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            canvas.Clear(new SKColor(13, 23, 42));
            canvas.DrawText(caption.Length > 32 ? caption[..32] : caption, 64, 72, title, paint);
            int y = 120, count = 0;
            foreach (string line in Wrap(text.Replace('\r', ' '), 44))
            {
                if (count++ == 8) { canvas.DrawText("… 전체 내용은 probe.log에 기록됩니다", 64, y, body, paint); break; }
                canvas.DrawText(line, 64, y, body, paint); y += 30;
            }
            int start = selected / 4 * 4;
            for (int i = start; i < Math.Min(start + 4, buttons.Length); i++)
            {
                int top = 414 + (i - start) * 52;
                paint.Color = i == selected ? new SKColor(32, 86, 145) : new SKColor(28, 40, 60);
                canvas.DrawRoundRect(new SKRect(64, top, 1216, top + 44), 6, 6, paint);
                paint.Color = SKColors.White;
                string label = buttons[i];
                canvas.DrawText((i == selected ? "▶ " : "   ") + (label.Length > 60 ? label[..60] : label), 82, top + 31, body, paint);
            }
            paint.Color = new SKColor(170, 196, 220);
            canvas.DrawText("↑↓ 선택   A 확인   B 취소   PLUS 종료", 64, 688, body, paint);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0) { yield return ""; continue; }
            for (int i = 0; i < line.Length; i += width) yield return line.Substring(i, Math.Min(width, line.Length - i));
        }
    }
}
