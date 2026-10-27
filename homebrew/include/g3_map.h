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
/* Original small-unit rules: ALPHA 10032900, 1008A680..1008A9B0.
 * terrain is elevation, not a terrain movement-price table. */
int g3_map_project(const G3Map *map,int x,int y,int *screen_x,int *screen_y);
int g3_map_step(const G3Map *map,int x,int y,int to_x,int to_y,int flying);
#define G3_ROUTE_MAX 4096u
/* Navigation workbench: occupied cells are excluded, including friendly cells.
 * This conservative occupancy policy is not yet the original battle policy.
 * -1 means unreachable/invalid; predecessor contains logical cell indices. */
int g3_map_routes(const G3Map *map,int x,int y,int flying,int budget,
                  const uint8_t *occupied,size_t count,int32_t *cost,int32_t *predecessor);
#endif
