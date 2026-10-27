#include "g3_field.h"
#include <stdlib.h>
#include <string.h>

int g3_field_renderer_open(G3FieldRenderer *r,G3Pack *pack) {
    if(!r || !pack || !pack->file || pack->count!=g3_field_asset_count())return 0;
    memset(r,0,sizeof(*r));
    r->pixels=calloc(pack->count,sizeof(*r->pixels));
    if(!r->pixels)return 0;
    r->pack=pack;return 1;
}
void g3_field_renderer_close(G3FieldRenderer *r) {
    if(!r)return;
    if(r->pixels && r->pack)for(unsigned i=0;i<r->pack->count;i++)free(r->pixels[i]);
    free(r->pixels);memset(r,0,sizeof(*r));
}
static int draw(G3FieldRenderer *r,int index,uint8_t *canvas,int dx,int dy,int mirror) {
    G3Asset a;
    if(index<0 || !g3_pack_record(r->pack,(unsigned)index,&a))return 0;
    if(!r->pixels[index]) {
        uint8_t *pixels=malloc(a.size);
        if(!pixels)return 0;
        if(!g3_pack_pixels(r->pack,&a,pixels,a.size)){free(pixels);return 0;}
        r->pixels[index]=pixels;
    }
    if(mirror)dx-=(int)a.width;
    int left=dx<0?-dx:0,top=dy<0?-dy:0;
    int right=(int)a.width,bottom=(int)a.height;
    if(dx+right>640)right=640-dx;
    if(dy+bottom>480)bottom=480-dy;
    for(int y=top;y<bottom;y++)for(int x=left;x<right;x++) {
        unsigned sx=mirror?a.width-1-(unsigned)x:(unsigned)x;
        const uint8_t *p=r->pixels[index]+((size_t)y*a.width+sx)*4;
        uint8_t *q=canvas+((size_t)(y+dy)*640+(unsigned)(x+dx))*4;
        for(unsigned c=0;c<3;c++)q[c]=(uint8_t)((p[c]*p[3]+q[c]*(255-p[3])+127)/255);
        q[3]=255;
    }
    return 1;
}
int g3_field_render(G3FieldRenderer *r,const G3Field *f,uint8_t *canvas,size_t capacity) {
    if(!r || !r->pixels || !f || !canvas || capacity<640u*480u*4u || !g3_field_valid(f))return 0;
    memset(canvas,0,640u*480u*4u);
    for(size_t i=3;i<640u*480u*4u;i+=4)canvas[i]=255;
    const G3Story *s=&f->view;
    if(s->background>=0 && !draw(r,g3_field_asset(1,s->background,0),canvas,-f->camera_x,-f->camera_y,0))return 0;
    unsigned order[4]={0,1,2,3};
    for(unsigned i=1;i<4;i++)for(unsigned j=i;j>0 && f->actors[order[j]].y<f->actors[order[j-1]].y;j--) {
        unsigned t=order[j];order[j]=order[j-1];order[j-1]=t;
    }
    for(unsigned i=0;i<4;i++) {
        int x,y,mirror,index=g3_field_actor_frame(f,order[i],&x,&y,&mirror);
        if(index>=0 && !draw(r,index,canvas,x,y,mirror))return 0;
    }
    int portraits[3]={s->left,s->right,s->center}, shifts[3]={-150,150,0};
    for(unsigned i=0;i<3;i++)if(portraits[i]>=0 && !draw(r,g3_field_asset(2,portraits[i],0),canvas,shifts[i],0,0))return 0;
    if(s->dialog>=0 && !draw(r,g3_field_asset(3,s->dialog,s->page),canvas,0,0,0))return 0;
    if(s->status && !draw(r,g3_field_asset(4,s->status,0),canvas,0,0,0))return 0;
    return 1;
}
