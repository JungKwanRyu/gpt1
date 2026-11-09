#include "raster.h"
#include <stddef.h>

// Same order/clipping as the game's SetPixel(dx+x,dy+y,src[y*sw+x]|0xff000000).
// Managed caller validates both array lengths and pins them for this call.
void wog_blit_opaque(uint32_t *dst, int dw, int dh, const uint32_t *src, int sw, int sh, int dx, int dy)
{
    if (!dst || !src || dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return;
    int64_t x0 = dx < 0 ? -(int64_t)dx : 0, y0 = dy < 0 ? -(int64_t)dy : 0;
    int64_t x1 = (int64_t)dw - dx, y1 = (int64_t)dh - dy;
    if (x1 > sw) x1 = sw;
    if (y1 > sh) y1 = sh;
    if (x0 >= x1 || y0 >= y1) return;
    for (int64_t y = y0; y < y1; y++) {
        const uint32_t *s = src + y * sw + x0;
        uint32_t *d = dst + (y + dy) * dw + x0 + dx;
        for (int64_t x = x0; x < x1; x++) *d++ = *s++ | UINT32_C(0xff000000);
    }
}

static unsigned minimum(unsigned a, unsigned b) { return a < b ? a : b; }
static unsigned add_floor(unsigned c) { return c <= 0x18 ? 0 : c < 0x30 ? (c - 0x18) * 2 : c; }

static uint32_t add_color(uint32_t d, uint32_t c, unsigned weight)
{
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8)
        result |= minimum(255, ((d >> shift) & 255) + add_floor((c >> shift) & 255) * weight / 256) << shift;
    return result;
}

static uint32_t mix_color(uint32_t d, uint32_t c, unsigned k, unsigned den)
{
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8)
        result |= ((((d >> shift) & 255) * (den - k) + ((c >> shift) & 255) * k) / den) << shift;
    return result;
}

static uint32_t ui_color(uint32_t d, uint32_t c, int mode)
{
    if (mode == 1 || mode == 2) return add_color(d, c, mode == 1 ? 256 : 100);
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8) {
        unsigned a = (d >> shift) & 255, b = (c >> shift) & 255, v = b;
        switch (mode) {
            case 3: v = a * 11 / 31; break;
            case 4: v = b * 15 / 31; break;
            case 5: v = b == 255 ? 255 : minimum(255, a * 256 / (256 - b)); break;
            case 6: { unsigned hi = a > b ? a : b, lo = minimum(a, b); v = minimum(255, hi + (255 - hi) * lo / 255); break; }
            case 7: v = minimum(255, a + b * b / 255); break;
        }
        result |= v << shift;
    }
    return result;
}

// Rectangles are clipped and array lengths checked by the managed bridge.
void wog_draw_frame(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                    int x0, int y0, int x1, int y1, int mode, int fade)
{
    for (int y = y0; y < y1; y++) {
        const uint32_t *s = src + (int64_t)(y - top) * sw + x0 - left;
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, s++, d++) {
            if (!(*s >> 24)) continue;
            uint32_t color = ui_color(*d, *s, mode);
            *d = fade < 256 ? mix_color(*d, color, (unsigned)fade, 256) : color;
        }
    }
}

void wog_fill_rect(uint32_t *dst, int dw, int x0, int y0, int x1, int y1, uint32_t color)
{
    unsigned alpha = color >> 24;
    if (!alpha) return;
    for (int y = y0; y < y1; y++) {
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, d++) *d = alpha == 255 ? color : mix_color(*d, color, alpha, 255);
    }
}

void wog_blit_masked(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                     int x0, int y0, int x1, int y1, int fade, int additive)
{
    for (int y = y0; y < y1; y++) {
        const uint32_t *s = src + (int64_t)(y - top) * sw + x0 - left;
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, s++, d++) {
            uint32_t color = *s; unsigned alpha = color >> 24;
            if (!alpha) continue;
            if (additive) { *d = add_color(*d, color, (unsigned)fade); continue; }
            if (fade < 256) { color = mix_color(*d, color, (unsigned)fade, 256); alpha = 255; }
            *d = alpha == 255 ? color : mix_color(*d, color, alpha, 255);
        }
    }
}
