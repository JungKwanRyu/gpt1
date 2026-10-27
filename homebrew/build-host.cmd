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
cl /nologo /W4 /WX /D_CRT_SECURE_NO_WARNINGS /LD /TC /Ihomebrew\include homebrew\source\g3_formats.c homebrew\source\g3_menu.c homebrew\source\g3_story.c /Fo:homebrew\build\ /Fe:homebrew\build\g3_formats.dll /link /EXPORT:g3_index_count /EXPORT:g3_index_record /EXPORT:g3_string /EXPORT:g3_pack_open /EXPORT:g3_pack_close /EXPORT:g3_pack_record /EXPORT:g3_pack_pixels /EXPORT:g3_menu_init /EXPORT:g3_menu_input /EXPORT:g3_menu_scene /EXPORT:g3_menu_render /EXPORT:g3_story_init /EXPORT:g3_story_tick /EXPORT:g3_story_asset /EXPORT:g3_story_identity /EXPORT:g3_story_asset_count /EXPORT:g3_story_save /EXPORT:g3_story_load /EXPORT:g3_story_blit
exit /b %errorlevel%
