#include "g3_field.h"
#include <string.h>

typedef struct { uint16_t word[7]; } G3Command;
typedef struct { uint16_t kind, condition_count, action_count; const G3Command *conditions,*actions; } G3Event;
typedef struct { int id,asset; } G3ResourceMap;
typedef struct { unsigned first,count; } G3TextPages;
typedef struct { int asset,x,y; } G3ActorFrame;
typedef struct { unsigned at,duration,frame; } G3ActorKey;
typedef struct { unsigned duration,count; const G3ActorKey *keys; } G3ActorAnimation;
typedef struct { unsigned id,frames,animations; const G3ActorFrame *frame; const G3ActorAnimation *animation; } G3ActorModel;
#include "g3_field2_data.h"
_Static_assert(G3_FIELD_ACTORS==4,"Field 2 actor state must match the decoded program");
_Static_assert(G3_STORY_EVENTS<=16,"Field event flags have a fixed checkpoint capacity");

uint32_t g3_field_identity(void) { return G3_STORY_ID; }
unsigned g3_field_asset_count(void) { return G3_STORY_ASSETS; }
int g3_field_asset(int kind,int id,int page) {
    if (kind==4) return id==1?G3_STORY_BOUNDARY_ASSET:G3_STORY_ERROR_ASSET;
    if (kind==1) {
        for (unsigned i=0;i<sizeof(g3_story_backgrounds)/sizeof(*g3_story_backgrounds);i++)
            if (g3_story_backgrounds[i].id==id) return g3_story_backgrounds[i].asset;
    } else if (kind==2) {
        for (unsigned i=0;i<sizeof(g3_story_portraits)/sizeof(*g3_story_portraits);i++)
            if (g3_story_portraits[i].id==id) return g3_story_portraits[i].asset;
    } else if (kind==3 && id>=0 && (unsigned)id<sizeof(g3_story_texts)/sizeof(*g3_story_texts) &&
               page>=0 && (unsigned)page<g3_story_texts[id].count) return (int)g3_story_texts[id].first+page;
    return -1;
}
static const G3ActorModel *model(unsigned actor) {
    if (actor>=G3_FIELD_ACTORS) return NULL;
    for (unsigned i=0;i<G3_FIELD_MODELS;i++)
        if (g3_field_models[i].id==(unsigned)g3_field_actor_init[actor][0]) return &g3_field_models[i];
    return NULL;
}
void g3_field_init(G3Field *f) {
    memset(f,0,sizeof(*f));
    G3Story *s=&f->view;
    s->background=G3_FIELD_BACKGROUND;
    s->left=s->right=s->center=s->dialog=s->track=-1;
    s->enabled[0]=1;
    for (unsigned i=0;i<G3_FIELD_ACTORS;i++) {
        f->actors[i].x=g3_field_actor_init[i][1];
        f->actors[i].y=g3_field_actor_init[i][2];
        f->actors[i].visible=1;
        f->actors[i].loop=1;
    }
}
int g3_field_actor_frame(const G3Field *f,unsigned index,int *x,int *y,int *mirror) {
    const G3ActorModel *m=model(index);
    if (!f || !m || !x || !y || !mirror) return -1;
    const G3FieldActor *actor=&f->actors[index];
    if (!actor->visible || actor->action<0 || (unsigned)actor->action>=m->animations) return -1;
    const G3ActorAnimation *animation=&m->animation[actor->action];
    if (!animation->duration) return -1;
    unsigned time=actor->loop?(unsigned)actor->clock%animation->duration:(unsigned)actor->clock;
    if (time>=animation->duration) time=animation->duration-1;
    for (unsigned i=0;i<animation->count;i++) {
        const G3ActorKey *key=&animation->keys[i];
        if (time>=key->at && time-key->at<key->duration && key->frame<m->frames) {
            const G3ActorFrame *frame=&m->frame[key->frame];
            *x=actor->x-f->camera_x+frame->x;
            *y=actor->y-f->camera_y+frame->y;
            *mirror=actor->mirror;
            /* A mirrored frame keeps its OBS anchor rather than its left edge. */
            if (*mirror) *x=actor->x-f->camera_x-frame->x;
            return frame->asset;
        }
    }
    return -1;
}
static int compare(int a,int b,unsigned mode) {
    switch (mode) { case 0:return a==b;case 1:return a!=b;case 2:return a<b;case 3:return a<=b;case 4:return a>b;case 5:return a>=b;default:return 0; }
}
static int ready(G3Story *s,const G3Event *event) {
    for (unsigned i=0;i<event->condition_count;i++) {
        const uint16_t *c=event->conditions[i].word;
        if (!c[0]) continue;
        if (c[0]!=100 || c[1]>=256 || c[3]>5) { s->status=2;s->error=c[0];return 0; }
        if (!compare(s->flags[c[1]],c[2],c[3])) return 0;
    }
    return 1;
}
static int moving(const G3Field *f) {
    if (f->camera_remaining) return 1;
    for (unsigned i=0;i<G3_FIELD_ACTORS;i++) if (f->actors[i].remaining) return 1;
    return 0;
}
void g3_field_tick(G3Field *f,int accept) {
    G3Story *s=&f->view;
    if (s->status) return;
    for (unsigned i=0;i<G3_FIELD_ACTORS;i++) {
        G3FieldActor *a=&f->actors[i];
        a->clock=(a->clock+1)%1000000;
        if (a->remaining) { a->x+=a->dx; a->y+=a->dy; a->remaining--; }
    }
    if (f->camera_remaining) {
        f->camera_x+=f->camera_dx;f->camera_y+=f->camera_dy;f->camera_remaining--;
        if (f->camera_x<0) f->camera_x=0;
        if (f->camera_x>660) f->camera_x=660; /* Original BGR 93 is 1300x500. */
        if (f->camera_y<0) f->camera_y=0;
        if (f->camera_y>20) f->camera_y=20;
    }
    if (s->timer>0) { s->timer--;return; }
    if (s->wait) {
        if (!accept) return;
        if ((unsigned)(s->page+1)<g3_story_texts[s->dialog].count) {s->page++;return;}
        s->wait=0;s->dialog=-1;s->page=0;
    }
    for (unsigned budget=0;budget<256;budget++) {
        if (s->active<0) {
            unsigned i;
            for (i=0;i<G3_STORY_EVENTS;i++) if (s->enabled[i] && !s->finished[i] && ready(s,&g3_story_events[i])) break;
            if (s->status) return;
            if (i==G3_STORY_EVENTS) {s->status=1;return;}
            s->active=(int)i;s->pc=0;
        }
        const G3Event *event=&g3_story_events[s->active];
        if ((unsigned)s->pc>=event->action_count) {s->finished[s->active]=1;s->active=-1;continue;}
        const uint16_t *c=event->actions[s->pc++].word;
        switch (c[0]) {
            case 0: if (c[1]>=G3_STORY_EVENTS) goto unsupported;s->enabled[c[1]]=1;break;
            case 1: if (moving(f)) {s->pc--;return;}break;
            case 2: s->timer=c[1];return;
            case 100: if(g3_field_asset(1,c[1],0)<0)goto unsupported;s->background=c[1];break;
            case 101: s->background=-1;break;
            case 103: break; /* Fade timing/palette is a presentation limitation. */
            case 200:case 201:case 202:
                if(g3_field_asset(2,c[1],0)<0 || g3_field_asset(3,c[2],0)<0)goto unsupported;
                if(c[0]==200)s->left=c[1];else if(c[0]==201)s->right=c[1];else s->center=c[1];
                s->dialog=c[2];s->page=0;s->wait=1;return;
            case 203:s->left=-1;break;case 204:s->right=-1;break;case 205:s->center=-1;break;
            case 300: {
                if(c[1]<10000 || c[1]>=10000+G3_FIELD_ACTORS)goto unsupported;
                G3FieldActor *a=&f->actors[c[1]-10000];const G3ActorModel *m=model(c[1]-10000);
                if (!m || c[2]>=m->animations || c[3]>1)goto unsupported;
                a->action=c[2];a->clock=0;a->loop=c[3];break;
            }
            case 302:
                if(c[1]>=G3_FIELD_ACTORS || c[4]>600)goto unsupported;
                f->actors[c[1]].dx=(int16_t)c[2];f->actors[c[1]].dy=(int16_t)c[3];
                f->actors[c[1]].remaining=c[4];break;
            case 303:
                if(c[1]>=G3_FIELD_ACTORS)goto unsupported;
                f->actors[c[1]].x=(int16_t)c[2];f->actors[c[1]].y=(int16_t)c[3];break;
            case 306:
                if(c[1]>=G3_FIELD_ACTORS)goto unsupported;
                f->actors[c[1]].mirror=!f->actors[c[1]].mirror;break;
            case 307:
                if(c[1]>=G3_FIELD_ACTORS)goto unsupported;
                f->actors[c[1]].loop=1;break;
            case 401:
                f->camera_dx=(int16_t)c[1];f->camera_dy=(int16_t)c[2];f->camera_remaining=c[3];break;
            case 700:s->track=c[1];break;
            case 703:s->next_field=c[1];break; /* This is NOT a field ID. */
            case 800:if(c[1]>=256)goto unsupported;s->flags[c[1]]=(uint8_t)c[2];break;
            case 500:s->status=1;return;
            default:
unsupported:s->status=2;s->error=c[0];s->pc--;return;
        }
    }
    s->status=2;s->error=65535;
}
int g3_field_valid(const G3Field *f) {
    const G3Story *s=&f->view;
    if(s->active< -1 || s->active>=(int)G3_STORY_EVENTS || s->pc<0 ||
       (s->active>=0 && (unsigned)s->pc>g3_story_events[s->active].action_count) ||
       s->status<0 || s->status>2 || s->wait<0 || s->wait>1 || s->timer<0 || s->timer>600 ||
       s->page<0 || s->next_field<0 || s->next_field>65535 || s->error<0 || s->error>65535 ||
       s->track< -1 || s->track>65535 || f->camera_x<0 || f->camera_x>660 ||
       f->camera_y<0 || f->camera_y>20 || f->camera_remaining<0 || f->camera_remaining>600 ||
       f->camera_dx< -32768 || f->camera_dx>32767 || f->camera_dy< -32768 || f->camera_dy>32767)return 0;
    if(s->background!=-1 && g3_field_asset(1,s->background,0)<0)return 0;
    if(s->left!=-1 && g3_field_asset(2,s->left,0)<0)return 0;
    if(s->right!=-1 && g3_field_asset(2,s->right,0)<0)return 0;
    if(s->center!=-1 && g3_field_asset(2,s->center,0)<0)return 0;
    if(s->dialog!=-1 && g3_field_asset(3,s->dialog,s->page)<0)return 0;
    if(s->wait && s->dialog<0)return 0;
    for(unsigned i=0;i<16;i++)if(s->enabled[i]>1 || s->finished[i]>1)return 0;
    for(unsigned i=0;i<G3_FIELD_ACTORS;i++) {
        const G3FieldActor *a=&f->actors[i];const G3ActorModel *m=model(i);
        if(!m || a->x< -32768 || a->x>32767 || a->y< -32768 || a->y>32767 ||
           a->action<0 || (unsigned)a->action>=m->animations || a->clock<0 || a->clock>=1000000 ||
           a->mirror<0 || a->mirror>1 || a->visible<0 || a->visible>1 || a->loop<0 || a->loop>1 ||
           a->remaining<0 || a->remaining>600 || a->dx< -32768 || a->dx>32767 || a->dy< -32768 || a->dy>32767)return 0;
    }
    return 1;
}
