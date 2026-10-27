"""Build the complete host shared library consumed by the ctypes regression tests."""
import argparse
import os
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parent.parent
SOURCES=['g3_formats','g3_menu','g3_story','g3_map','g3_combat_math',
         'g3_field','g3_field_render','g3_progress','g3_audio','g3_navigation','g3_combat','g3_battle_events','g3_aftermath','g3_session']


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cc',default=os.environ.get('CC','gcc'))
    args=parser.parse_args()
    (ROOT/'homebrew/build').mkdir(exist_ok=True)
    if os.name=='nt':
        subprocess.run(['cmd','/c',str(ROOT/'homebrew/build-host.cmd')],cwd=ROOT,check=True)
    else:
        subprocess.run([args.cc,'-std=c11','-O2','-Wall','-Wextra','-Werror','-fPIC','-shared',
                        '-Ihomebrew/include',*[f'homebrew/source/{n}.c' for n in SOURCES],
                        '-o','homebrew/build/g3_formats.dll'],cwd=ROOT,check=True)
