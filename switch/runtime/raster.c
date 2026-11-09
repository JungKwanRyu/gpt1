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
