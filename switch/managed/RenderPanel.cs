using SkiaSharp;

namespace WogSwitch;

internal static class RenderPanel
{
    internal const int Width = 1280, Height = 720;
    internal const string Korean = "창세기전 스위치 포팅";
    internal const string Sample = "한글 이미지 렌더링 테스트";

    internal static SKBitmap Create(string fontFile, bool pressed, string[]? status = null, bool diagnostics = true)
    {
        if (!File.Exists(fontFile)) throw new FileNotFoundException("Bundled Korean font missing", fontFile);
        using var face = SKTypeface.FromFile(fontFile) ?? throw new InvalidOperationException("Korean font load failed");
        if (diagnostics) foreach (char c in Korean + Sample)
            if (!char.IsWhiteSpace(c) && !face.ContainsGlyph(c))
                throw new InvalidOperationException("Missing Korean glyph U+" + ((int)c).ToString("X4"));
        if (diagnostics) Program.Log("PASS Korean glyph coverage: all sample Hangul characters present");
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(13, 23, 42));
            using var paint = new SKPaint { IsAntialias = true, Color = pressed ? new SKColor(180, 40, 50) : new SKColor(32, 86, 145) };
            canvas.DrawRect(0, 0, Width, 150, paint);
            paint.Color = SKColors.White;
            using var title = new SKFont(face, 54) { Edging = SKFontEdging.Antialias };
            canvas.DrawText(Korean, 64, 98, title, paint);
            using var body = new SKFont(face, 34) { Edging = SKFontEdging.Antialias };
            canvas.DrawText(diagnostics ? Sample : "게임 실행 준비", 64, 218, body, paint);
            paint.Color = new SKColor(170, 196, 220);
            using var small = new SKFont(face, 25) { Edging = SKFontEdging.Antialias };
            canvas.DrawText(diagnostics ? "SkiaSharp 3.119 / native Switch raster" : "0.5.17", 64, 264, small, paint);
            canvas.DrawText(pressed ? "A 입력 확인 — 색상 변경 성공" : "A 버튼: 색상 변경    PLUS: 종료", 64, 638, body, paint);
            canvas.DrawText(diagnostics ? "이미지: PNG 저장 → 디코딩 → 확대 출력" : "X: 게임 시작    PLUS: 종료", 64, 690, small, paint);

            if (diagnostics)
            {
            // Exercise the actual PNG encoder and decoder used by the runtime.
            using var tile = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var tileCanvas = new SKCanvas(tile))
            {
                tileCanvas.Clear(SKColors.White);
                using var square = new SKPaint { Color = new SKColor(26, 166, 146) };
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    if (((x + y) & 1) == 0) tileCanvas.DrawRect(x * 8, y * 8, 8, 8, square);
            }
            using var image = SKImage.FromBitmap(tile);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("PNG encoding failed");
            using var decoded = SKBitmap.Decode(encoded.ToArray()) ?? throw new InvalidOperationException("PNG decoding failed");
            if (decoded.Width != 64 || decoded.Height != 64 || decoded.GetPixel(4, 4) != tile.GetPixel(4, 4))
                throw new InvalidOperationException("PNG pixel roundtrip mismatch");
            canvas.DrawBitmap(decoded, new SKRect(64, 306, 320, 562));
            Program.Log("PASS PNG encode/decode: dimensions and sample pixel preserved");
            paint.Color = SKColors.White;
            canvas.DrawText("한글 가나다라마", 380, 366, body, paint);
            canvas.DrawText("이미지와 글꼴이 함께 표시됩니다", 380, 422, small, paint);
            canvas.DrawText("0.5 — X 버튼으로 게임 시작 시도", 380, 478, small, paint);
            }
            if (status != null)
            {
                paint.Color = new SKColor(13, 23, 42);
                canvas.DrawRect(360, 306, 900, 258, paint);
                paint.Color = SKColors.White;
                using var detail = new SKFont(face, 25) { Edging = SKFontEdging.Antialias };
                for (int i = 0; i < Math.Min(4, status.Length); i++)
                    canvas.DrawText(status[i], 380, 350 + i * 56, detail, paint);
            }
            canvas.Flush();

            // Reject blank/missing output rather than counting a glyph lookup alone.
            if (diagnostics)
            {
            int changed = 0;
            unsafe
            {
                uint* pixels = (uint*)bitmap.GetPixels();
                int stride = bitmap.RowBytes / sizeof(uint);
                uint background = pixels[170 * stride + 20];
                for (int y = 180; y < 225; y++) for (int x = 64; x < 850; x++)
                    if (pixels[y * stride + x] != background) changed++;
            }
            if (changed < 100) throw new InvalidOperationException("Korean raster output is blank");
            Program.Log("PASS Korean text raster: " + changed + " non-background pixels");
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    internal static void Save(SKBitmap bitmap, string filename)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Panel PNG encoding failed");
        using var file = File.Create(filename);
        encoded.SaveTo(file);
        Program.Log("PASS rendered PNG saved: " + filename);
    }
}
