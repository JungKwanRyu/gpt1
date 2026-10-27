#include "g3_formats.h"
#include <limits.h>
#include <string.h>

static uint16_t le16(const uint8_t *p) {
    return (uint16_t)((unsigned)p[0] | (unsigned)p[1] << 8);
}
static uint32_t le32(const uint8_t *p) {
    return (uint32_t)p[0] | (uint32_t)p[1]<<8 | (uint32_t)p[2]<<16 | (uint32_t)p[3]<<24;
}
static int safe_name(const uint8_t *p, size_t size) {
    size_t i;
    for (i=0; i<size && p[i]; i++)
        if (p[i]<32 || p[i]>126 || p[i]=='/' || p[i]=='\\' || p[i]==':') return 0;
    return i>0 && i<size && !(i==1 && p[0]=='.') && !(i==2 && p[0]=='.' && p[1]=='.');
}
int g3_index_count(const uint8_t *data, size_t size, uint32_t *count) {
    if (!data || !count || size<4) return 0;
    *count=le32(data);
    return (uint64_t)*count*30+4==size;
}
int g3_index_record(const uint8_t *data, size_t size, uint32_t index, G3IndexRecord *record) {
    uint32_t count;
    const uint8_t *p;
    if (!record || !g3_index_count(data,size,&count) || index>=count) return 0;
    p=data+4+(size_t)index*30;
    if (!safe_name(p,9) || !safe_name(p+9,13)) return 0;
    memset(record,0,sizeof(*record));
    memcpy(record->name,p,9); memcpy(record->archive,p+9,13);
    record->start=le32(p+22); record->end=le32(p+26);
    return record->end>=record->start;
}
int g3_string(const uint8_t *data, size_t size, uint32_t index,
              uint8_t *output, size_t capacity, size_t *length) {
    size_t count, base, at, n=0;
    if (!data || size<4 || !length) return 0;
    count=le16(data); base=4+count*2;
    if (base+(size_t)le16(data+2)!=size || index>=count) return 0;
    at=base+le16(data+4+index*2);
    if (at>=size) return 0;
    while (at+n<size && data[at+n]!=255) n++;
    if (at+n==size || !output || capacity<=n) return 0;
    *length=n;
    for (n=0; n<*length; n++) output[n]=(uint8_t)(data[at+n]^255);
    output[*length]=0;
    return 1;
}
int g3_pack_open(G3Pack *pack, const char *path) {
    uint8_t header[16];
    long size;
    if (!pack || !path) return 0;
    memset(pack,0,sizeof(*pack));
    pack->file=fopen(path,"rb");
    if (!pack->file) return 0;
    if (fseek(pack->file,0,SEEK_END) || (size=ftell(pack->file))<16 ||
        fseek(pack->file,0,SEEK_SET) || fread(header,1,16,pack->file)!=16 ||
        memcmp(header,"G3NXRAW1",8) || le32(header+12)!=0) goto fail;
    pack->file_size=(uint64_t)size;
    pack->count=le32(header+8);
    if (!pack->count || pack->count>4096 || 16+(uint64_t)pack->count*32>pack->file_size) goto fail;
    return 1;
fail:
    g3_pack_close(pack); return 0;
}
void g3_pack_close(G3Pack *pack) {
    if (pack && pack->file) fclose(pack->file);
    if (pack) memset(pack,0,sizeof(*pack));
}
int g3_pack_record(G3Pack *pack, uint32_t index, G3Asset *asset) {
    uint8_t rec[32];
    if (!pack || !pack->file || !asset || index>=pack->count) return 0;
    if (fseek(pack->file,(long)(16+index*32),SEEK_SET) || fread(rec,1,32,pack->file)!=32) return 0;
    if (!safe_name(rec,16)) return 0;
    memset(asset,0,sizeof(*asset)); memcpy(asset->name,rec,16);
    asset->width=le32(rec+16); asset->height=le32(rec+20);
    asset->offset=le32(rec+24); asset->size=le32(rec+28);
    return asset->width>0 && asset->width<=1040 && asset->height>0 && asset->height<=624 &&
        (uint64_t)asset->width*asset->height*4==asset->size &&
        asset->offset>=16+(uint64_t)pack->count*32 &&
        (uint64_t)asset->offset+asset->size<=pack->file_size;
}
int g3_pack_pixels(G3Pack *pack, const G3Asset *asset, uint8_t *pixels, size_t capacity) {
    if (!pack || !pack->file || !asset || !pixels || asset->size>capacity ||
        (uint64_t)asset->offset+asset->size>pack->file_size) return 0;
#if LONG_MAX < UINT32_MAX
    if (asset->offset>(uint32_t)LONG_MAX) return 0;
#endif
    return !fseek(pack->file,(long)asset->offset,SEEK_SET) &&
        fread(pixels,1,asset->size,pack->file)==asset->size;
}
