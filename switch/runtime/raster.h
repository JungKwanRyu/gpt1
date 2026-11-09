#pragma once
#include <stdint.h>
void wog_blit_opaque(uint32_t *dst, int dw, int dh, const uint32_t *src, int sw, int sh, int dx, int dy);
void wog_draw_frame(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                    int x0, int y0, int x1, int y1, int mode, int fade);
void wog_fill_rect(uint32_t *dst, int dw, int x0, int y0, int x1, int y1, uint32_t color);
void wog_blit_masked(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                     int x0, int y0, int x1, int y1, int fade, int additive);
