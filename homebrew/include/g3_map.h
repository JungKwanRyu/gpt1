#ifndef G3_MAP_H
#define G3_MAP_H
#include <stddef.h>
#include <stdint.h>

/* The caller retains the immutable OBT byte buffer for the view's lifetime.
 * Viewport coordinates refer to the base bitmap, not battle/world coordinates.
 * Object layers, elevation, collision, and world projection are separate. */
typedef struct {
    const uint8_t *cells, *tiles, *terrain, *attributes;
    uint32_t columns, rows, tile_count, width, height;
    int32_t origin_x, origin_y;
} G3Map;
int g3_map_open(G3Map *map, const uint8_t *data, size_t size);
int g3_map_render(const G3Map *map, int x, int y,
                  uint8_t *rgba, size_t capacity, unsigned width, unsigned height);
#endif
