#ifndef G3_ANIMATION_H
#define G3_ANIMATION_H
#include <stdint.h>
typedef struct { int asset,x,y; } G3BattleFrame;
typedef struct { uint16_t word[9]; } G3ObsCommand;
typedef struct { uint32_t duration,count; const G3ObsCommand *commands; } G3ObsAction;
typedef struct {
    int model;unsigned frames,actions;
    const G3BattleFrame *frame;const G3ObsAction *action;
} G3BattleModel;
#endif
