#ifndef G3_MENU_H
#define G3_MENU_H
#include <stddef.h>
#include <stdint.h>
enum { G3_UP=1, G3_DOWN=2, G3_LEFT=4, G3_RIGHT=8, G3_ACCEPT=16, G3_BACK=32, G3_EXIT=64 };
enum { G3_TITLE, G3_EPISODES, G3_NOTICE, G3_STORY, G3_LOAD };
typedef struct {
    int screen, selected, episode, notice, quit;
} G3Menu;
void g3_menu_init(G3Menu *menu);
void g3_menu_input(G3Menu *menu, unsigned buttons);
unsigned g3_menu_scene(const G3Menu *menu);
void g3_menu_render(uint32_t *screen, size_t stride, const uint8_t *rgba);
#endif
