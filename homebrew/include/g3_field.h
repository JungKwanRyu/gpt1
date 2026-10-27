#ifndef G3_FIELD_H
#define G3_FIELD_H
#include "g3_story.h"
#include "g3_formats.h"
#define G3_FIELD_ACTOR_FIELDS(X) X(x) X(y) X(action) X(clock) X(mirror) X(visible) X(dx) X(dy) X(remaining) X(loop)
typedef struct {
#define DECLARE(n) int32_t n;
    G3_FIELD_ACTOR_FIELDS(DECLARE)
#undef DECLARE
} G3FieldActor;
typedef struct {
    G3Story view;
    int32_t camera_x, camera_y, camera_dx, camera_dy, camera_remaining;
    G3FieldActor actors[4];
} G3Field;
void g3_field_init(G3Field *field);
void g3_field_tick(G3Field *field, int accept);
int g3_field_valid(const G3Field *field);
int g3_field_asset(int kind,int id,int page);
uint32_t g3_field_identity(void);
unsigned g3_field_asset_count(void);
int g3_field_actor_frame(const G3Field *field,unsigned actor,int *x,int *y,int *mirror);
typedef struct { G3Pack *pack; uint8_t **pixels; } G3FieldRenderer;
int g3_field_renderer_open(G3FieldRenderer *renderer,G3Pack *pack);
void g3_field_renderer_close(G3FieldRenderer *renderer);
int g3_field_render(G3FieldRenderer *renderer,const G3Field *field,uint8_t *canvas,size_t capacity);
#endif
