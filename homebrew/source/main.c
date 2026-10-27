/* Native title frontend reconstructed from original BGR/OBS graphics. */
#include <switch.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "g3_formats.h"
#include "g3_menu.h"
#include "g3_story.h"
#include "g3_session.h"
#include "g3_audio.h"
#include "g3_navigation.h"
#include "ui_font.h"

#define PIXEL_CAPACITY (640u*480u*4u)

static void error_screen(const char *message) {
    consoleInit(NULL);
    printf("G3NX - The War of Genesis III\n\n%s\n\n",message);
    printf("Required: sdmc:/switch/g3nx/g3nx.title\n");
    printf("Copy the complete 0.7.0 package including battle6.obt and g3nx.battle6.\n\nPLUS: exit\n");
    PadState pad;
    padConfigureInput(1,HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad)&HidNpadButton_Plus) break;
        consoleUpdate(NULL);
    }
    consoleExit(NULL);
}

static int load_scene(G3Pack *pack, unsigned scene, uint8_t *pixels) {
    G3Asset asset;
    return g3_pack_record(pack,scene,&asset) && asset.width==640 && asset.height==480 &&
        g3_pack_pixels(pack,&asset,pixels,PIXEL_CAPACITY);
}

static int open_story(G3Pack *pack) {
    G3Asset first;
    char expected[17];
    if (pack->file) return 1;
    if (!g3_pack_open(pack,"sdmc:/switch/g3nx/g3nx.story")) return 0;
    snprintf(expected,sizeof(expected),"id%08x",g3_story_identity());
    if (pack->count!=g3_story_asset_count() || !g3_pack_record(pack,0,&first) || strcmp(first.name,expected)) {
        g3_pack_close(pack); return 0;
    }
    return 1;
}
static int open_field(G3Pack *pack) {
    G3Asset first;char expected[17];
    if(pack->file)return 1;
    if(!g3_pack_open(pack,"sdmc:/switch/g3nx/g3nx.field2"))return 0;
    snprintf(expected,sizeof(expected),"id%08x",g3_field_identity());
    if(pack->count!=g3_field_asset_count() || !g3_pack_record(pack,0,&first) || strcmp(first.name,expected)) {
        g3_pack_close(pack);return 0;
    }
    return 1;
}
static int open_aftermath(G3Pack *pack) {
    G3Asset first;char expected[17];
    if(pack->file)return 1;
    if(!g3_pack_open(pack,"sdmc:/switch/g3nx/g3nx.field3"))return 0;
    snprintf(expected,sizeof(expected),"id%08x",g3_aftermath_identity());
    if(pack->count!=g3_aftermath_asset_count() || !g3_pack_record(pack,0,&first) || strcmp(first.name,expected)) {
        g3_pack_close(pack);return 0;
    }
    return 1;
}
static int story_layer(G3Pack *pack,int index,uint8_t *layer) {
    G3Asset asset;
    return index>=0 && g3_pack_record(pack,(unsigned)index,&asset) &&
        asset.width==640 && asset.height==480 && g3_pack_pixels(pack,&asset,layer,PIXEL_CAPACITY);
}
static int render_story(G3Pack *pack,const G3Story *story,uint8_t *canvas,uint8_t *layer,int aftermath) {
    int (*asset)(int,int,int)=aftermath?g3_aftermath_asset:g3_story_asset;
    unsigned i;
    int portraits[3]={story->left,story->right,story->center};
    const int shifts[3]={-150,150,0};
    memset(canvas,0,PIXEL_CAPACITY);
    for (i=3;i<PIXEL_CAPACITY;i+=4) canvas[i]=255;
    if (story->background>=0) {
        if (!story_layer(pack,asset(1,story->background,0),layer)) return 0;
        memcpy(canvas,layer,PIXEL_CAPACITY);
    }
    for (i=0;i<3;i++) if (portraits[i]>=0) {
        if (!story_layer(pack,asset(2,portraits[i],0),layer)) return 0;
        g3_story_blit(canvas,layer,shifts[i]);
    }
    if (story->dialog>=0) {
        if (!story_layer(pack,asset(3,story->dialog,story->page),layer)) return 0;
        g3_story_blit(canvas,layer,0);
    }
    if (story->status) {
        if (!story_layer(pack,asset(4,story->status,0),layer)) return 0;
        g3_story_blit(canvas,layer,0);
    }
    return 1;
}
static void status_text(uint32_t *screen,uint32_t stride,const char *s) {
    int x=12;
    for (;*s && x<140;s++,x+=8) {
        unsigned c=(unsigned char)*s;
        if (c<32 || c>126) continue;
        for (unsigned y=0;y<14;y++) for (unsigned bit=0;bit<8;bit++)
            if (ui_font[c-32][y]&(1u<<bit)) screen[(690+y)*stride+x+bit]=0xffe0f0ff;
    }
}

int main(int argc, char **argv) {
    (void)argc; (void)argv;
    G3Pack pack;
    G3Pack story_pack={0};
    G3Progress progress;
    G3Story aftermath;
    G3Pack aftermath_pack={0};
    g3_progress_init(&progress);g3_aftermath_init(&aftermath);
    G3Pack field_pack={0};
    G3FieldRenderer field_renderer={0};
    G3Navigation navigation={0};
    int navigating=0;
    int audio_requested=-2,audio_ok=1,muted=0;
    G3Menu menu;
    Framebuffer fb;
    PadState pad;
    uint8_t *pixels=NULL;
    uint8_t *layer=NULL;
    int last_view[7]={0};
    int story_dirty=1, toast=0, toast_frames=0;
    unsigned loaded_scene=0, stick_last=0, stick_frames=0;
    int failed=0;
    if (!g3_pack_open(&pack,"sdmc:/switch/g3nx/g3nx.title")) {
        error_screen("Cannot open title assets, or file header is invalid."); return 1;
    }
    G3Asset first;
    if (pack.count!=12 || !g3_pack_record(&pack,0,&first) || strcmp(first.name,"title0")) {
        g3_pack_close(&pack); error_screen("Title assets do not match this version."); return 1;
    }
    g3_menu_init(&menu);
    pixels=malloc(PIXEL_CAPACITY);
    if (!pixels || !load_scene(&pack,loaded_scene,pixels)) {
        free(pixels); g3_pack_close(&pack); error_screen("Cannot load title scene."); return 1;
    }
    Result rc=framebufferCreate(&fb,nwindowGetDefault(),1280,720,PIXEL_FORMAT_RGBA_8888,2);
    if (R_FAILED(rc)) {
        free(pixels); g3_pack_close(&pack); error_screen("Cannot create framebuffer."); return 1;
    }
    rc=framebufferMakeLinear(&fb);
    if (R_FAILED(rc)) {
        framebufferClose(&fb); free(pixels); g3_pack_close(&pack);
        error_screen("Cannot create linear framebuffer."); return 1;
    }
    padConfigureInput(1,HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        uint64_t down=padGetButtonsDown(&pad);
        unsigned buttons=0;
        if (down&HidNpadButton_Plus) buttons|=G3_EXIT;
        if (down&HidNpadButton_A) buttons|=G3_ACCEPT;
        if (down&HidNpadButton_B) buttons|=G3_BACK;
        if (down&HidNpadButton_Up) buttons|=G3_UP;
        if (down&HidNpadButton_Down) buttons|=G3_DOWN;
        if (down&HidNpadButton_Left) buttons|=G3_LEFT;
        if (down&HidNpadButton_Right) buttons|=G3_RIGHT;
        HidAnalogStickState stick=padGetStickPos(&pad,0);
        unsigned direction=0;
        if (stick.y>16000) direction=G3_UP;
        else if (stick.y< -16000) direction=G3_DOWN;
        else if (stick.x< -16000) direction=G3_LEFT;
        else if (stick.x>16000) direction=G3_RIGHT;
        if (direction!=stick_last) { stick_frames=0; buttons|=direction; }
        else if (direction && ++stick_frames>=20 && (stick_frames-20)%7==0) buttons|=direction;
        stick_last=direction;
        int old_screen=menu.screen;
        g3_menu_input(&menu,buttons);
        if (menu.quit) break;
        int resumed=0;
        if (menu.screen==G3_LOAD) {
            if (open_story(&story_pack) &&
                (navigation.map_data || g3_navigation_open(&navigation,"sdmc:/switch/g3nx")) &&
                g3_session_load(&navigating,&progress,&navigation,&aftermath,"sdmc:/switch/g3nx")) {
                menu.screen=G3_STORY; menu.episode=0; resumed=1;
            } else { menu.screen=G3_NOTICE; menu.notice=3; }
        }
        if (menu.screen==G3_STORY) {
            int entering=old_screen!=G3_STORY;
            if (entering) {
                if(!resumed)navigating=0;
                if (!open_story(&story_pack)) { failed=1; break; }
                if (!layer) layer=malloc(PIXEL_CAPACITY);
                if (!layer) { failed=1; break; }
                if (!resumed) {g3_progress_init(&progress);g3_aftermath_init(&aftermath);}
                if(navigating==2 && !open_aftermath(&aftermath_pack)){failed=1;break;}
                if(!open_field(&field_pack)) {failed=1;break;}
                if(!field_renderer.pack && !g3_field_renderer_open(&field_renderer,&field_pack)){failed=1;break;}
                story_dirty=1; toast=0; toast_frames=0;
            }
            if(!navigating && progress.stage==1 && progress.field.view.status==G3_STORY_BOUNDARY &&
               !entering && (buttons&G3_ACCEPT)) {
                if(!navigation.map_data && !g3_navigation_open(&navigation,"sdmc:/switch/g3nx")){failed=1;break;}
                if(!g3_navigation_reset(&navigation)){failed=1;break;}
                navigating=1;buttons&=~G3_ACCEPT;toast_frames=0;
            }
            if(!navigating && progress.stage==0 && progress.story.status==G3_STORY_BOUNDARY && !entering && (buttons&G3_ACCEPT)) {
                /* The original CPR calls field 1, then field 2, then battle 6.
                 * Never interpret FLD opcode 703 as the destination field. */
                progress.stage=1;story_dirty=1;
            }
            if(navigating==1 && navigation.events.outcome==0 && !entering && (buttons&G3_ACCEPT)) {
                if(!open_aftermath(&aftermath_pack)){failed=1;break;}
                g3_aftermath_init(&aftermath);navigating=2;buttons&=~G3_ACCEPT;
            }
            if(navigating==1) {
                unsigned input=buttons;
                if(down&HidNpadButton_L)input|=G3_NAV_PREVIOUS;
                if(down&HidNpadButton_R)input|=G3_NAV_NEXT;
                if(down&HidNpadButton_ZR)input|=G3_NAV_RESET;
                if(down&HidNpadButton_ZL)input|=G3_NAV_TOGGLE;
                if(!g3_navigation_input(&navigation,input) || !g3_navigation_tick(&navigation) || !g3_navigation_render(&navigation,pixels,PIXEL_CAPACITY)){failed=1;break;}
            } else if(navigating==2)g3_aftermath_tick(&aftermath,!entering && (buttons&G3_ACCEPT));
            else if(progress.stage==0)g3_story_tick(&progress.story,!entering && (buttons&G3_ACCEPT));
            else g3_field_tick(&progress.field,!entering && (buttons&G3_ACCEPT));
            if (down&HidNpadButton_X) {
                toast=g3_session_save(navigating,&progress,&navigation,&aftermath,"sdmc:/switch/g3nx")?1:2;
                toast_frames=180;
            }
            const G3Story *current=navigating==2?&aftermath:progress.stage?&progress.field.view:&progress.story;
            int view[7]={current->background,current->left,current->right,current->center,current->dialog,current->page,current->status};
            if (navigating!=1 && (navigating==2 || progress.stage || story_dirty || memcmp(view,last_view,sizeof(view)))) {
                if (navigating==2 ? !render_story(&aftermath_pack,&aftermath,pixels,layer,1) : progress.stage ? !g3_field_render(&field_renderer,&progress.field,pixels,PIXEL_CAPACITY) :
                    !render_story(&story_pack,&progress.story,pixels,layer,0)) { failed=1; break; }
                memcpy(last_view,view,sizeof(view)); story_dirty=0;
            }
        } else {
            unsigned scene=g3_menu_scene(&menu);
            if (scene!=loaded_scene || old_screen==G3_STORY) {
                if (!load_scene(&pack,scene,pixels)) { failed=1; break; }
                loaded_scene=scene;
            }
        }
        if(down&HidNpadButton_Y)muted=!muted;
        int requested=-1;
        if(menu.screen==G3_STORY && !muted)requested=navigating==1?6:navigating==2?aftermath.track:progress.stage?progress.field.view.track:progress.story.track;
        if(requested!=audio_requested){audio_ok=g3_audio_set_track(requested);audio_requested=requested;}
        if(menu.screen==G3_STORY && navigating==1 && !muted) {
            int sounds[8];unsigned count=g3_navigation_sounds(&navigation,sounds,8);
            for(unsigned i=0;i<count;i++)if(!g3_audio_play_effect(sounds[i]))audio_ok=0;
        }
        g3_audio_pump();
        uint32_t stride_bytes;
        uint32_t *screen=framebufferBegin(&fb,&stride_bytes);
        g3_menu_render(screen,stride_bytes/4,pixels);
        if(menu.screen==G3_STORY && navigating==1 && toast_frames<=0) {
            char stats[32];int selected=navigation.selected;
            snprintf(stats,sizeof(stats),"P%d MP%d/%d",selected+1,(int)navigation.remaining[selected],(int)navigation.units[selected].movement);
            status_text(screen,stride_bytes/4,navigation.combat_mode?"BATTLE 6":stats);
        } else if (menu.screen==G3_STORY && toast_frames>0) {
            status_text(screen,stride_bytes/4,toast==1?"SAVED":"SAVE FAILED");
            toast_frames--;
        }
        if(menu.screen==G3_STORY && toast_frames<=0 && (muted || !audio_ok))
            status_text(screen,stride_bytes/4,muted?"MUTED":"AUDIO ERROR");
        framebufferEnd(&fb);
    }
    g3_audio_shutdown();g3_navigation_close(&navigation);g3_field_renderer_close(&field_renderer);g3_pack_close(&field_pack);
    framebufferClose(&fb); free(pixels); free(layer); g3_pack_close(&pack); g3_pack_close(&story_pack);g3_pack_close(&aftermath_pack);
    if (failed) error_screen("Required scene assets are missing, corrupt, or incompatible.");
    return failed;
}
