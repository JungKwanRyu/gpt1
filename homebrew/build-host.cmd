@echo off
setlocal
cd /d "%~dp0.."
if not exist homebrew\build mkdir homebrew\build
if defined VSCMD_VER goto compile
if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat" (
    call "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat" >nul
) else (
    echo Run this file from an x64 Native Tools Command Prompt for Visual Studio.
    exit /b 1
)
:compile
cl /nologo /W4 /WX /D_CRT_SECURE_NO_WARNINGS /LD /TC /Ihomebrew\include homebrew\source\g3_formats.c homebrew\source\g3_menu.c homebrew\source\g3_story.c homebrew\source\g3_map.c homebrew\source\g3_combat_math.c homebrew\source\g3_field.c homebrew\source\g3_field_render.c homebrew\source\g3_progress.c homebrew\source\g3_audio.c homebrew\source\g3_navigation.c homebrew\source\g3_combat.c /Fo:homebrew\build\ /Fe:homebrew\build\g3_formats.dll /link /EXPORT:g3_combat_morale_stat /EXPORT:g3_combat_init /EXPORT:g3_combat_next /EXPORT:g3_combat_pass /EXPORT:g3_combat_moved /EXPORT:g3_combat_strike /EXPORT:g3_navigation_attackable /EXPORT:g3_map_project /EXPORT:g3_map_step /EXPORT:g3_map_routes /EXPORT:g3_navigation_open /EXPORT:g3_navigation_close /EXPORT:g3_navigation_reset /EXPORT:g3_navigation_input /EXPORT:g3_navigation_render /EXPORT:g3_index_count /EXPORT:g3_index_record /EXPORT:g3_string /EXPORT:g3_pack_open /EXPORT:g3_pack_close /EXPORT:g3_pack_record /EXPORT:g3_pack_pixels /EXPORT:g3_menu_init /EXPORT:g3_menu_input /EXPORT:g3_menu_scene /EXPORT:g3_menu_render /EXPORT:g3_story_init /EXPORT:g3_story_tick /EXPORT:g3_story_asset /EXPORT:g3_story_identity /EXPORT:g3_story_asset_count /EXPORT:g3_story_save /EXPORT:g3_story_load /EXPORT:g3_story_blit /EXPORT:g3_map_open /EXPORT:g3_map_render /EXPORT:g3_original_rand /EXPORT:g3_original_damage_variation /EXPORT:g3_story_valid /EXPORT:g3_field_init /EXPORT:g3_field_tick /EXPORT:g3_field_valid /EXPORT:g3_field_asset /EXPORT:g3_field_identity /EXPORT:g3_field_asset_count /EXPORT:g3_field_actor_frame /EXPORT:g3_field_renderer_open /EXPORT:g3_field_renderer_close /EXPORT:g3_field_render /EXPORT:g3_progress_init /EXPORT:g3_progress_save /EXPORT:g3_progress_load /EXPORT:g3_pcm_open /EXPORT:g3_pcm_read /EXPORT:g3_pcm_close
exit /b %errorlevel%
