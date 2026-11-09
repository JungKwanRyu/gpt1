#!/usr/bin/env python3
"""Exercise the actual backdrop C kernel with clipping and UBSan."""
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = r'''
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <limits.h>
#include "raster.h"
static uint32_t state = 123;
static uint32_t random32(void) { return state = state * 1664525u + 1013904223u; }
int main(void) {
  uint32_t source[32*32], actual[40*40], expected[40*40];
  for (int k=0; k<4000; k++) {
    int dw=1+random32()%40, dh=1+random32()%40, sw=1+random32()%32, sh=1+random32()%32;
    int dx=(int)(random32()%101)-50, dy=(int)(random32()%101)-50;
    if (k%127==0) dx=INT_MIN;
    if (k%131==0) dy=INT_MAX;
    for (int i=0;i<sw*sh;i++) source[i]=random32();
    for (int i=0;i<dw*dh;i++) actual[i]=expected[i]=random32();
    for (int y=0;y<sh;y++) for (int x=0;x<sw;x++) {
      int64_t xx=(int64_t)x+dx, yy=(int64_t)y+dy;
      if (xx>=0 && xx<dw && yy>=0 && yy<dh) expected[yy*dw+xx]=source[y*sw+x]|0xff000000u;
    }
    wog_blit_opaque(actual,dw,dh,source,sw,sh,dx,dy);
    if (memcmp(actual,expected,(size_t)dw*dh*4)) return 1;
  }
  wog_blit_opaque(NULL,1,1,source,1,1,0,0);
  wog_blit_opaque(actual,1,1,NULL,1,1,0,0);
  wog_blit_opaque(actual,-1,1,source,1,1,0,0);
  puts("PASS real native backdrop: 4000 reference-equivalent clipping/alpha cases, extreme coordinates, null/invalid input, UBSan");
  return 0;
}
'''
with tempfile.TemporaryDirectory(prefix="wog-raster-") as tmp:
    path = Path(tmp)
    (path / 'main.c').write_text(source)
    subprocess.run(['gcc', '-std=c11', '-O2', '-Wall', '-Wextra', '-fsanitize=undefined',
                    '-fno-sanitize-recover=all', '-I' + str(root / 'switch/runtime'),
                    str(path / 'main.c'), str(root / 'switch/runtime/raster.c'), '-o', str(path / 'test')], check=True)
    subprocess.run([str(path / 'test')], check=True)
