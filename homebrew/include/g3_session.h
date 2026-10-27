#ifndef G3_SESSION_H
#define G3_SESSION_H
#include "g3_progress.h"
#include "g3_navigation.h"
#include "g3_aftermath.h"
/* stage: 0 first two fields, 1 battle 6, 2 field 3. */
int g3_session_save(int stage,const G3Progress *progress,const G3Navigation *battle,const G3Story *aftermath,const char *directory);
int g3_session_load(int *stage,G3Progress *progress,G3Navigation *battle,G3Story *aftermath,const char *directory);
#endif
