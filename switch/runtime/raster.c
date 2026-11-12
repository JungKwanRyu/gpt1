#include "raster.h"
#include <stddef.h>
_Static_assert(sizeof(WogFieldPiece) == 40 && offsetof(WogFieldPiece, sx) == 32, "Field piece ABI mismatch");

// Same order/clipping as the game's SetPixel(dx+x,dy+y,src[y*sw+x]|0xff000000).
// Managed caller validates both array lengths and pins them for this call.
void wog_blit_opaque(uint32_t *dst, int dw, int dh, const uint32_t *src, int sw, int sh, int dx, int dy)
{
    if (!dst || !src || dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return;
    int64_t x0 = dx < 0 ? -(int64_t)dx : 0, y0 = dy < 0 ? -(int64_t)dy : 0;
    int64_t x1 = (int64_t)dw - dx, y1 = (int64_t)dh - dy;
    if (x1 > sw) x1 = sw;
    if (y1 > sh) y1 = sh;
    if (x0 >= x1 || y0 >= y1) return;
    for (int64_t y = y0; y < y1; y++) {
        const uint32_t *s = src + y * sw + x0;
        uint32_t *d = dst + (y + dy) * dw + x0 + dx;
        for (int64_t x = x0; x < x1; x++) *d++ = *s++ | UINT32_C(0xff000000);
    }
}

static unsigned minimum(unsigned a, unsigned b) { return a < b ? a : b; }
static unsigned add_floor(unsigned c) { return c <= 0x18 ? 0 : c < 0x30 ? (c - 0x18) * 2 : c; }

static uint32_t add_color(uint32_t d, uint32_t c, unsigned weight)
{
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8)
        result |= minimum(255, ((d >> shift) & 255) + add_floor((c >> shift) & 255) * weight / 256) << shift;
    return result;
}

static uint32_t mix_color(uint32_t d, uint32_t c, unsigned k, unsigned den)
{
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8)
        result |= ((((d >> shift) & 255) * (den - k) + ((c >> shift) & 255) * k) / den) << shift;
    return result;
}

static uint32_t ui_color(uint32_t d, uint32_t c, int mode)
{
    if (mode == 1 || mode == 2) return add_color(d, c, mode == 1 ? 256 : 100);
    uint32_t result = UINT32_C(0xff000000);
    for (int shift = 0; shift <= 16; shift += 8) {
        unsigned a = (d >> shift) & 255, b = (c >> shift) & 255, v = b;
        switch (mode) {
            case 3: v = a * 11 / 31; break;
            case 4: v = b * 15 / 31; break;
            case 5: v = b == 255 ? 255 : minimum(255, a * 256 / (256 - b)); break;
            case 6: { unsigned hi = a > b ? a : b, lo = minimum(a, b); v = minimum(255, hi + (255 - hi) * lo / 255); break; }
            case 7: v = minimum(255, a + b * b / 255); break;
        }
        result |= v << shift;
    }
    return result;
}

// Rectangles are clipped and array lengths checked by the managed bridge.
void wog_draw_frame(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                    int x0, int y0, int x1, int y1, int mode, int fade)
{
    for (int y = y0; y < y1; y++) {
        const uint32_t *s = src + (int64_t)(y - top) * sw + x0 - left;
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, s++, d++) {
            if (!(*s >> 24)) continue;
            uint32_t color = ui_color(*d, *s, mode);
            *d = fade < 256 ? mix_color(*d, color, (unsigned)fade, 256) : color;
        }
    }
}

void wog_fill_rect(uint32_t *dst, int dw, int x0, int y0, int x1, int y1, uint32_t color)
{
    unsigned alpha = color >> 24;
    if (!alpha) return;
    for (int y = y0; y < y1; y++) {
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, d++) *d = alpha == 255 ? color : mix_color(*d, color, alpha, 255);
    }
}

void wog_blit_masked(uint32_t *dst, int dw, const uint32_t *src, int sw, int left, int top,
                     int x0, int y0, int x1, int y1, int fade, int additive)
{
    for (int y = y0; y < y1; y++) {
        const uint32_t *s = src + (int64_t)(y - top) * sw + x0 - left;
        uint32_t *d = dst + (int64_t)y * dw + x0;
        for (int x = x0; x < x1; x++, s++, d++) {
            uint32_t color = *s; unsigned alpha = color >> 24;
            if (!alpha) continue;
            if (additive) { *d = add_color(*d, color, (unsigned)fade); continue; }
            if (fade < 256) { color = mix_color(*d, color, (unsigned)fade, 256); alpha = 255; }
            *d = alpha == 255 ? color : mix_color(*d, color, alpha, 255);
        }
    }
}

void wog_field_backdrop(uint32_t *dst, int dw, int dh, const uint32_t *bg, int bw, int bh,
                        int cx, int cy, int ox, int oy, const WogFieldPiece *pieces, int count)
{
    for (int y = 0; y < 480; y++) {
        int64_t fy = (int64_t)oy + y, wy = (int64_t)y + cy;
        if (fy < 0 || fy >= dh) continue;
        for (int x = 0; x < 640; x++) {
            int64_t fx = (int64_t)ox + x, wx = (int64_t)x + cx;
            if (fx < 0 || fx >= dw) continue;
            uint32_t *to = dst + fy * dw + fx;
            if (wy >= 0 && wy < bh && wx >= 0 && wx < bw) {
                uint32_t color = bg[wy * bw + wx];
                if ((color & UINT32_C(0x00f8fcf8)) != UINT32_C(0x00f8fc00)) {
                    *to = color | UINT32_C(0xff000000); continue;
                }
            }
            for (int i = 0; i < count; i++) {
                const WogFieldPiece *p = pieces + i;
                int64_t lx = wx - p->x, ly = wy - p->y;
                if (lx < 0 || lx >= p->w || ly < 0 || ly >= p->h) continue;
                int px = (int)(((lx + p->sx) % p->pw + p->pw) % p->pw);
                int py = (int)(((ly + p->sy) % p->ph + p->ph) % p->ph);
                *to = p->pixels[(int64_t)py * p->pw + px] | UINT32_C(0xff000000);
                break;
            }
        }
    }
}

void wog_transform(uint32_t *dst, int dw, int x0, int y0, int x1, int y1, int mode, uint32_t color, int power)
{
    for (int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) {
        uint32_t c=dst[y*dw+x], out;
        if(mode==0) {
            unsigned g=(7*((c>>16)&255)+2*((c>>8)&255)+(c&255))/10;
            out=0xff000000u|g<<16|g<<8|g;
        } else {
            out=mode==1?0xff000000u:c&0xff000000u;
            for(int s=0;s<=16;s+=8) {
                int v=(c>>s)&255;
                int n=mode==1?v*power/31:v+(((int)((color>>s)&255)-v)*power)/31;
                out|=(uint32_t)n<<s;
            }
        }
        dst[y*dw+x]=out;
    }
}

void wog_blit_scaled(uint32_t *dst,int dw,int dh,const uint32_t *src,int sw,int sh,int left,int top,int w,int h)
{
 for(int y=0;y<h;y++){int dy=top+y;if(dy<0||dy>=dh)continue;int sy=y*sh/h;
  for(int x=0;x<w;x++){int dx=left+x;if(dx>=0&&dx<dw)dst[dy*dw+dx]=src[sy*sw+x*sw/w]|0xff000000u;}}
}

void wog_field_wipe(uint32_t *dst,int dw,int dh,const uint32_t *under,const uint32_t *over,int ox,int oy,int mode,int way,int tick,int ticks)
{
 const int W=640,H=480;int denominator=ticks>0?ticks:1;
 if(mode==907){
  for(int y=0;y<H;y++)for(int x=0;x<W;x++)if(ox+x>=0&&ox+x<dw&&oy+y>=0&&oy+y<dh)dst[(oy+y)*dw+ox+x]=under[y*W+x];
  int cols=way/1000>0?way/1000:1,rows=way%1000>0?way%1000:1,r=ticks-tick,g=r/10;
  for(int j=0;j<rows;j++)for(int i=0;i<cols;i++){
   int sx0=i*W/cols,sx1=(i+1)*W/cols,sy0=j*H/rows,sy1=(j+1)*H/rows;
   int dx=(cols/2-i)*r/2,dy=(rows/2-j)*r/2,x0=sx0-dx-g,x1=sx1-dx+g,y0=sy0-dy-g,y1=sy1-dy+g;
   if(x1<=0||y1<=0||x0>=W||y0>=H||x1<=x0||y1<=y0||sx1<=sx0||sy1<=sy0)continue;
   for(int y=y0>0?y0:0;y<(y1<H?y1:H);y++){int sy=sy0+(y-y0)*(sy1-sy0)/(y1-y0);
    for(int x=x0>0?x0:0;x<(x1<W?x1:W);x++)if(ox+x>=0&&ox+x<dw&&oy+y>=0&&oy+y<dh)
      dst[(oy+y)*dw+ox+x]=over[sy*W+sx0+(x-x0)*(sx1-sx0)/(x1-x0)];}
  }return;
 }
 int pos=0;if(mode==904){int span=(way==2||way==3)?W:H;pos=span*tick/denominator;if(pos<0)pos=0;if(pos>span)pos=span;if(!pos)return;}
 unsigned char lit[480]={0};
 if(mode==908){
  for(int t=0;t<=(tick<30?tick:30);t++)for(int c=0;c<=480;c+=120){int a=c+2*t,b=c-2*t;if(a>=0&&a<H)lit[a]=1;if(b>=0&&b<H)lit[b]=1;}
  for(int u=0;u<=(tick<60?tick:60)-30;u++)for(int c=0;c<=480;c+=120){int a=c+1+2*u,b=c-1-2*u;if(a>=0&&a<H)lit[a]=1;if(b>=0&&b<H)lit[b]=1;}
 }
 for(int y=0;y<H;y++)for(int x=0;x<W;x++){
  int dx=ox+x,dy=oy+y;if(dx<0||dx>=dw||dy<0||dy>=dh)continue;
  uint32_t color;
  if(mode==909){int k=tick*8/denominator+1;if(k>8)k=8;unsigned a=k>=8?256u:(unsigned)(4*k*256/31);uint32_t b=under[y*W+x],o=over[y*W+x];color=0xff000000u;
   for(int s=0;s<=16;s+=8)color|=((((b>>s)&255)*(256-a)+((o>>s)&255)*a)>>8)<<s;
  }else if(mode==901){int edge=(W+way)*tick/denominator;color=(x<edge?over:under)[y*W+x]|0xff000000u;
  }else if(mode==903){int bands=way>0?way:1,width=W/bands,band=x/width;if(band>bands-1)band=bands-1;int shown=tick-band;if(shown<0)shown=0;if(shown>width)shown=width;color=(x-band*width<shown?over:under)[y*W+x]|0xff000000u;
  }else if(mode==908){color=(lit[y]?over:under)[y*W+x];
  }else{int sx=x,sy=y;if(way==0){if(sy<H-pos)sy=H-pos;}else if(way==1){if(sy>pos-1)sy=pos-1;}else if(way==2){if(sx<W-pos)sx=W-pos;}else if(sx>pos-1)sx=pos-1;color=over[sy*W+sx]|0xff000000u;}
  dst[dy*dw+dx]=color;
 }
}

void wog_ui_scaled(uint32_t *dst,int dw,const uint32_t *src,int sw,int sh,int left,int top,int width,int height,int x0,int y0,int x1,int y1,int fade,int tint)
{
 for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
  uint32_t c=src[(int64_t)(y-top)*sh/height*sw+(int64_t)(x-left)*sw/width];if(!(c&0xff000000u))continue;
  uint32_t d=dst[y*dw+x],out=0xff000000u;
  for(int s=0;s<=16;s+=8){int v=(c>>s)&255;
   if(tint>=0){v=v*32/(32-tint);if(v>255)v=255;}
   if(fade<256)v=(v*fade+((d>>s)&255)*(256-fade))/256;
   out|=(uint32_t)v<<s;
  }dst[y*dw+x]=out;
 }
}

void wog_moses_fade(uint32_t *dst,int dw,const uint32_t *from,int cam_y,int x0,int y0,int x1,int y1,int mode,int power)
{
 for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
  int i=y*dw+x;uint32_t c=dst[i],out;
  if(mode==2){unsigned b=c&255;b+=(255-b)*power/31;out=(c&0xffffff00u)|b;}
  else {out=0xff000000u;uint32_t a=mode==0?from[(y-cam_y)*dw+x]:0;
   for(int s=0;s<=16;s+=8){unsigned v=(c>>s)&255;v=mode==0?((((a>>s)&255)*power+v*(256-power))>>8):v*(31-power)/31;out|=v<<s;}
  }dst[i]=out;
 }
}
