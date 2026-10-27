#ifndef G3_PROGRESS_H
#define G3_PROGRESS_H
#include "g3_field.h"
typedef struct { int32_t stage; G3Story story; G3Field field; } G3Progress;
void g3_progress_init(G3Progress *progress);
int g3_progress_save(const G3Progress *progress,const char *directory);
int g3_progress_load(G3Progress *progress,const char *directory);
#endif
