#pragma once
#include <stdint.h>
void wog_blit_opaque(uint32_t *dst, int dw, int dh, const uint32_t *src, int sw, int sh, int dx, int dy);
