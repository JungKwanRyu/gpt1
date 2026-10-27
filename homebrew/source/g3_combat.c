#include "g3_combat.h"
#include "g3_combat_math.h"
#include <string.h>

int32_t g3_combat_morale_stat(int32_t stat,int32_t morale) {
    /* 1003EB80/1003EBE0/1003ED60. IDIV truncates toward zero. */
    int64_t value=(int64_t)stat+((150-(int64_t)morale)*-(stat/2))/150;
    if(value<0)return 0;
    if(value>INT32_MAX)return INT32_MAX;
    return (int32_t)value;
}
static int supported(const G3CombatStats *s) {
    if(s->max_hp<=0 || s->max_hp>1000000 || s->period<2 || s->period>255 ||
       s->team<1 || s->team>2 || s->hits<1 || s->hits>16 || s->critical<0 || s->critical>100 ||
       s->flags<0 || s->flags>15 || s->range<0 || s->range>100 ||
       s->element<0 || s->element>5 || s->secondary<0 || s->secondary>4 ||
       s->min_range<0 || s->min_range>s->range ||
       s->height_cost<0 || s->height_cost>1 || s->bonus<0 || s->bonus>1000)return 0;
    const int32_t values[]={s->strength,s->dexterity,s->armor,s->power,s->modifier,
                            s->accuracy,s->block_accuracy,s->block_defense};
    for(unsigned i=0;i<sizeof(values)/sizeof(values[0]);i++)if(values[i]<0 || values[i]>10000)return 0;
    for(unsigned i=0;i<9;i++)if(s->scales[i]<0 || s->scales[i]>20)return 0;
    return 1;
}
static int running(const G3Combat *c) {
    return c && c->count>0 && c->count<=G3_COMBAT_MAX && !c->status;
}
int g3_combat_next(G3Combat *c) {
    if(!running(c) || c->phase>4 || c->clock==0 || c->clock>UINT32_MAX-256)return 0;
    int teams=0;
    for(unsigned i=0;i<c->count;i++)if(c->hp[i]>0)teams|=1<<c->stats[i].team;
    /* Workbench termination, NOT the original BTL win/loss event. */
    if(teams!=6){c->active=-1;c->status=1;return 1;}
    for(unsigned ticks=0;ticks<=255;ticks++) {
        unsigned start=c->active<0?0:(unsigned)c->active;
        for(unsigned phase=0;phase<5;phase++) {
            for(unsigned i=start;i<c->count;i++) {
                const G3CombatStats *s=&c->stats[i];
                if(c->hp[i]>0 && (unsigned)s->team==c->phase && c->clock%(unsigned)s->period==0 &&
                   (c->move[i] || c->attack[i])) {c->active=(int)i;return 1;}
            }
            c->phase=(c->phase+1)%5;start=0;
        }
        /* 10030120 increments the global battle clock after an empty scan.
         * 10035070 restores action flags when clock % period == 1. */
        c->clock++;c->phase=0;c->active=-1;
        for(unsigned i=0;i<c->count;i++)if(c->clock%(unsigned)c->stats[i].period==1) {
            c->move[i]=1;c->attack[i]=1;
        }
    }
    return 0;
}
int g3_combat_init(G3Combat *c,const G3CombatStats *stats,unsigned count,uint32_t seed) {
    if(!c || !stats || !count || count>G3_COMBAT_MAX)return 0;
    for(unsigned i=0;i<count;i++)if(!supported(&stats[i]))return 0;
    memset(c,0,sizeof(*c));memcpy(c->stats,stats,count*sizeof(*stats));
    c->count=count;c->clock=1;c->rng=seed;c->active=-1;c->last_source=-1;c->last_target=-1;
    for(unsigned i=0;i<count;i++){c->hp[i]=stats[i].max_hp;c->move[i]=1;c->attack[i]=1;}
    return g3_combat_next(c);
}
int g3_combat_pass(G3Combat *c) {
    if(!running(c) || c->active<0 || (unsigned)c->active>=c->count)return 0;
    c->move[c->active]=0;c->attack[c->active]=0;
    return g3_combat_next(c);
}
int g3_combat_moved(G3Combat *c) {
    if(!running(c) || c->active<0 || (unsigned)c->active>=c->count || !c->move[c->active])return 0;
    c->move[c->active]=0;
    return c->attack[c->active]?1:g3_combat_next(c);
}
int g3_combat_strike(G3Combat *c,unsigned target) {
    if(!running(c) || c->active<0 || (unsigned)c->active>=c->count || target>=c->count ||
       target==(unsigned)c->active || c->hp[target]<=0 || !c->attack[c->active] ||
       c->stats[target].team==c->stats[c->active].team)return 0;
    unsigned source=(unsigned)c->active;
    const G3CombatStats *a=&c->stats[source],*d=&c->stats[target];
    if(!supported(a) || !supported(d))return 0;
    int str=g3_combat_morale_stat(a->strength,80),dex=g3_combat_morale_stat(a->dexterity,80);
    int defense=g3_combat_morale_stat(d->armor,80),ddex=g3_combat_morale_stat(d->dexterity,80);
    int denominator=a->accuracy+dex;
    if(!denominator && !(a->flags&G3_UNBLOCKABLE) && (d->flags&7))return 0;
    uint32_t rng=c->rng;
    int hp=c->hp[target],hits=0,criticals=0,state=2;
    int64_t total=0;
    for(int i=0;i<a->hits && hp>0;i++) {
        hits++;state=2;
        /* 10040F20: no RNG consumption when dodge ability is absent. */
        if(!(a->flags&G3_UNBLOCKABLE) && (d->flags&G3_DODGE) &&
           (int)(g3_original_rand(&rng)%100)<(ddex/2)*200/denominator) {state=5;continue;}
        int armor=defense;
        /* 10040E30: passive defense, without active status 257/25B. */
        if(!(a->flags&G3_UNBLOCKABLE)) {
            int numerator=d->block_accuracy+ddex,blocked=0;
            if(d->flags&G3_BLOCK)blocked=(int)(g3_original_rand(&rng)%100)<(numerator/2)*200/denominator;
            else if(d->flags&G3_PERFECT_BLOCK)blocked=(int)(g3_original_rand(&rng)%100)<numerator*100/denominator;
            if(blocked){armor+=d->block_defense;state=4;}
        }
        /* 1003F37A and attack handler bonus at +44, then 1004153C..10041751.
         * Supported inputs bound all intermediates; no signed overflow. */
        int64_t damage=(int64_t)(a->power+10)*str+(int64_t)a->modifier*a->modifier+(int64_t)a->bonus*a->bonus;
        if(a->element)damage=damage*d->scales[a->element-1]/10;
        if(a->secondary)damage=damage*d->scales[a->secondary+4]/10;
        if(armor)damage/=armor;
        if(damage>INT32_MAX/4)return 0;
        int value=g3_original_damage_variation((int32_t)damage,&rng);
        if(value>0 && state==2 && (int)(g3_original_rand(&rng)%100)<a->critical){value*=2;criticals++;}
        total+=value;hp=value>=hp?0:hp-value;
        if(total>INT32_MAX)return 0;
    }
    c->hp[target]=hp;c->rng=rng;c->attack[source]=0;
    c->last_source=(int)source;c->last_target=(int)target;c->last_damage=(int32_t)total;
    c->last_hits=hits;c->last_critical=criticals;c->last_state=state;
    if(!c->move[source] || hp==0)return g3_combat_next(c);
    return 1;
}
