#ifndef G3_PROGRESS_H
#define G3_PROGRESS_H
#include "g3_field.h"
typedef struct { int32_t stage; G3Story story; G3Field field; } G3Progress;
void g3_progress_init(G3Progress *progress);
int g3_progress_save(const G3Progress *progress,const char *directory);
int g3_progress_load(G3Progress *progress,const char *directory);
#define G3_PROGRESS_BYTES 872u
int g3_progress_valid(const G3Progress *p);
uint32_t g3_progress_identity(void);
int g3_progress_encode(uint8_t *out,const G3Progress *p);
int g3_progress_decode(G3Progress *p,const uint8_t *in);
void g3_story_encode_state(uint8_t *out,const G3Story *p);
void g3_story_decode_state(G3Story *p,const uint8_t *in);
#endif
