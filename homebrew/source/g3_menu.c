#include "g3_menu.h"
#include <string.h>

void g3_menu_init(G3Menu *menu) {
    memset(menu,0,sizeof(*menu));
}
void g3_menu_input(G3Menu *menu, unsigned buttons) {
    if (buttons&G3_EXIT) { menu->quit=1; return; }
    if (menu->screen==G3_STORY) {
        if (buttons&G3_BACK) menu->screen=G3_EPISODES;
        return;
    }
    if (menu->screen==G3_LOAD) {
        if (buttons&G3_BACK) menu->screen=G3_TITLE;
        return;
    }
    if (menu->screen==G3_NOTICE) {
        if (buttons&(G3_BACK|G3_ACCEPT))
            menu->screen=menu->notice<3 ? G3_EPISODES : G3_TITLE;
        return;
    }
    if (menu->screen==G3_EPISODES) {
        if (buttons&G3_BACK) { menu->screen=G3_TITLE; return; }
        if (buttons&(G3_LEFT|G3_UP)) menu->episode=(menu->episode+2)%3;
        else if (buttons&(G3_RIGHT|G3_DOWN)) menu->episode=(menu->episode+1)%3;
        if (buttons&G3_ACCEPT) {
            menu->notice=menu->episode;
            menu->screen=menu->episode==0?G3_STORY:G3_NOTICE;
        }
        return;
    }
    if (buttons&G3_UP) menu->selected=(menu->selected+3)%4;
    else if (buttons&G3_DOWN) menu->selected=(menu->selected+1)%4;
    if (buttons&G3_ACCEPT) {
        switch (menu->selected) {
            case 0: menu->screen=G3_EPISODES; break;
            case 1: menu->screen=G3_LOAD; break;
            case 2: menu->notice=4; menu->screen=G3_NOTICE; break;
            case 3: menu->quit=1; break;
        }
    }
}
unsigned g3_menu_scene(const G3Menu *menu) {
    if (menu->screen==G3_LOAD) return 10;
    if (menu->screen==G3_EPISODES) return 4u+(unsigned)menu->episode;
    if (menu->screen==G3_NOTICE) return 7u+(unsigned)menu->notice;
    return (unsigned)menu->selected;
}
void g3_menu_render(uint32_t *screen, size_t stride, const uint8_t *rgba) {
    /* Preserve 640x480 aspect ratio: 960x720 with 160px sidebars. */
    unsigned x,y;
    for (y=0;y<720;y++) {
        for (x=0;x<160;x++) screen[y*stride+x]=0xff000000u;
        for (x=1120;x<1280;x++) screen[y*stride+x]=0xff000000u;
        for (x=0;x<960;x++) {
            const uint8_t *p=rgba+((size_t)(y*2/3)*640+x*2/3)*4;
            screen[y*stride+160+x]=0xff000000u | (uint32_t)p[2]<<16 | (uint32_t)p[1]<<8 | p[0];
        }
    }
}
