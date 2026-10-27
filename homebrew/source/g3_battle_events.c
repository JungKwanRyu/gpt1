#include "g3_navigation.h"
#include <string.h>
typedef struct { uint16_t word[7]; } G3BattleCommand;
typedef struct { unsigned conditions,actions; const G3BattleCommand *condition,*action; } G3BattleEventDef;
#include "g3_battle6_program.h"

void g3_battle_events_init(G3BattleEvents *s) {
    memset(s,0,sizeof(*s));s->active=0;s->dialog=s->speaker=s->outcome=s->match_unit=-1;s->enabled[0]=1;
}
static int actor(const G3Navigation *n,unsigned reference) {
    if(reference>=10000 && reference<10022)return (int)reference-10000;
    if(reference==20010)return n->events.match_unit;
    for(unsigned i=0;i<22;i++)if((unsigned)n->units[i].character==reference && n->combat.hp[i]>0)return (int)i;
    return -1;
}
static int ready(G3Navigation *n,const G3BattleEventDef *event) {
    G3BattleEvents *s=&n->events;
    for(unsigned i=0;i<event->conditions;i++) {
        const uint16_t *c=event->condition[i].word;
        if(c[0]==1) {if(s->intro_done)return 0;}
        else if(c[0]==301) {
            for(unsigned j=0;j<22;j++)if(n->combat.hp[j]>0 && n->units[j].team==c[1])return 0;
        } else if(c[0]==302 && c[1]==20001) {
            int found=-1;
            for(unsigned j=0;j<22;j++) {
                const G3BattleUnit *u=&n->units[j];
                if(n->combat.hp[j]>0 && u->team==4 && u->x>=(c[2]&255) && u->x<=(c[3]&255) &&
                   u->y>=(c[2]>>8) && u->y<=(c[3]>>8)){found=(int)j;break;}
            }
            if(found<0)return 0;
            s->match_unit=found;
        } else if(c[0]==200) {
            int a=actor(n,c[1]),b=actor(n,c[2]);
            if(a<0 || b<0 || n->combat.hp[a]<=0 || n->combat.hp[b]<=0)return 0;
            const G3BattleUnit *u=&n->units[a],*v=&n->units[b];
            if(u->x!=v->x && u->y!=v->y)return 0;
            int dx=u->x-v->x,dy=u->y-v->y;
            int dh=(int)n->map.terrain[(unsigned)u->y*n->map.width+(unsigned)u->x]-
                (int)n->map.terrain[(unsigned)v->y*n->map.width+(unsigned)v->x];
            if(dx<0)dx=-dx;
            if(dy<0)dy=-dy;
            if(dh<0)dh=-dh;
            if(40*(dx+dy)+20*dh>80)return 0;
        } else {s->error=c[0];return 0;}
    }
    return 1;
}
int g3_battle_events_tick(G3Navigation *n,int accept) {
    if(!n)return 0;
    G3BattleEvents *s=&n->events;
    if(s->error)return 0;
    if(s->outcome>=0)return 1;
    if(s->wait) {
        if(!accept)return 1;
        s->dialog=s->speaker=-1;s->wait=0;
    }
    for(unsigned budget=0;budget<256;budget++) {
        if(s->active<0) {
            unsigned i;
            for(i=0;i<G3_BATTLE_EVENTS;i++)if(s->enabled[i] && !s->finished[i] && ready(n,&battle_events[i]))break;
            if(s->error)return 0;
            if(i==G3_BATTLE_EVENTS)return 1;
            s->active=(int)i;s->pc=0;
        }
        const G3BattleEventDef *e=&battle_events[s->active];
        if((unsigned)s->pc>=e->actions) {s->finished[s->active]=1;s->active=-1;continue;}
        const uint16_t *c=e->action[s->pc++].word;
        switch(c[0]) {
            case 0:if(c[1]>=G3_BATTLE_EVENTS)goto error;s->enabled[c[1]]=1;break;
            case 1:break;
            case 100:s->outcome=c[1];s->ready=0;return 1;
            case 200:
                s->speaker=actor(n,c[1]);s->dialog=c[2];
                if(s->speaker<0 || c[2]>=G3_BATTLE_TEXTS)goto error;
                s->wait=1;return 1;
            case 509: {
                int who=actor(n,c[1]);
                if(who<0 || c[2]>4)goto error;
                n->units[who].team=c[2];n->combat.stats[who].team=c[2];break;
            }
            case 706:s->ready=1;s->intro_done=1;break;
            default:goto error;
        }
        continue;
error:s->error=c[0];return 0;
    }
    s->error=65535;return 0;
}
int g3_battle_events_valid(const G3BattleEvents *s) {
    if(!s || s->active< -1 || s->active>=(int)G3_BATTLE_EVENTS || s->pc<0 ||
       (s->active>=0 && (unsigned)s->pc>battle_events[s->active].actions) ||
       s->dialog< -1 || s->dialog>=(int)G3_BATTLE_TEXTS || s->speaker< -1 || s->speaker>=22 ||
       s->wait<0 || s->wait>1 || (s->wait && (s->dialog<0 || s->speaker<0)) ||
       (s->outcome!= -1 && s->outcome!=0 && s->outcome!=3) || s->ready<0 || s->ready>1 ||
       s->intro_done<0 || s->intro_done>1 || s->match_unit< -1 || s->match_unit>=22 || s->error)return 0;
    for(unsigned i=0;i<16;i++)if(s->enabled[i]>1 || s->finished[i]>1 || (i>=G3_BATTLE_EVENTS && (s->enabled[i] || s->finished[i])))return 0;
    return 1;
}
