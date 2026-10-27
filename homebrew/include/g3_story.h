#ifndef G3_STORY_H
#define G3_STORY_H
#include <stdint.h>
#define G3_STORY_FIELDS(X) X(active) X(pc) X(background) X(left) X(right) X(center) X(dialog) X(page) X(wait) X(timer) X(status) X(next_field) X(error) X(track)
typedef struct {
#define G3_DECLARE(name) int32_t name;
    G3_STORY_FIELDS(G3_DECLARE)
#undef G3_DECLARE
    uint8_t flags[256], enabled[16], finished[16];
} G3Story;
enum { G3_STORY_RUNNING, G3_STORY_BOUNDARY, G3_STORY_ERROR };
void g3_story_init(G3Story *story);
void g3_story_tick(G3Story *story, int accept);
int g3_story_valid(const G3Story *story);
int g3_story_asset(int kind, int id, int page);
uint32_t g3_story_identity(void);
unsigned g3_story_asset_count(void);
int g3_story_save(const G3Story *story, const char *directory);
int g3_story_load(G3Story *story, const char *directory);
void g3_story_blit(uint8_t *canvas, const uint8_t *layer, int dx);
#endif
