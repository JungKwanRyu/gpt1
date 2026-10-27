#ifndef G3_FORMATS_H
#define G3_FORMATS_H
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

typedef struct {
    char name[10];
    char archive[14];
    uint32_t start, end;
} G3IndexRecord;

/* Read the measured original IDX format without native-struct casts. */
int g3_index_count(const uint8_t *data, size_t size, uint32_t *count);
int g3_index_record(const uint8_t *data, size_t size, uint32_t index, G3IndexRecord *record);
/* Returns XOR-decoded CP949 bytes; Unicode conversion is the caller's job. */
int g3_string(const uint8_t *data, size_t size, uint32_t index,
              uint8_t *output, size_t capacity, size_t *length);

typedef struct {
    char name[17];
    uint32_t width, height, offset, size;
} G3Asset;
typedef struct {
    FILE *file;
    uint32_t count;
    uint64_t file_size;
} G3Pack;

int g3_pack_open(G3Pack *pack, const char *path);
void g3_pack_close(G3Pack *pack);
int g3_pack_record(G3Pack *pack, uint32_t index, G3Asset *asset);
int g3_pack_pixels(G3Pack *pack, const G3Asset *asset, uint8_t *pixels, size_t capacity);
#endif
