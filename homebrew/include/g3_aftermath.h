#ifndef G3_AFTERMATH_H
#define G3_AFTERMATH_H
#include "g3_story.h"
void g3_aftermath_init(G3Story *scene);
void g3_aftermath_tick(G3Story *scene,int accept);
int g3_aftermath_valid(const G3Story *scene);
int g3_aftermath_asset(int kind,int id,int page);
uint32_t g3_aftermath_identity(void);
unsigned g3_aftermath_asset_count(void);
#endif
