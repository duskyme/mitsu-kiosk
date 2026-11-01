#include "cp9550_spool.h"

#include <stdlib.h>
#include <string.h>

static const uint8_t kFooter9550S[4] = { 0x1B, 0x50, 0x47, 0x00 };

static const cp9550_media_info kMedia[] = {
    { "4x6", 0x02, 2152, 1416 },
    { "5x7", 0x04, 1812, 2452 },
    { "6x9", 0x05, 2152, 2792 },
};

static void write_be16(uint8_t *p, uint16_t v)
{
    p[0] = (uint8_t)(v >> 8);
    p[1] = (uint8_t)(v & 0xFF);
}

const cp9550_media_info *cp9550_media_get(enum cp9550_media media)
{
    if ((unsigned)media >= (unsigned)(sizeof(kMedia) / sizeof(kMedia[0])))
        return NULL;
    return &kMedia[media];
}

int cp9550_split_planar_rgb(
    const uint8_t *rgb,
    size_t rgb_len,
    int cols,
    int rows,
    uint8_t *out_r,
    uint8_t *out_g,
    uint8_t *out_b)
{
    size_t plane;
    size_t i;

    if (!rgb || !out_r || !out_g || !out_b || cols <= 0 || rows <= 0)
        return -1;

    plane = (size_t)cols * (size_t)rows;
    if (rgb_len < plane * 3)
        return -2;

    for (i = 0; i < plane; i++) {
        size_t o = i * 3;
        out_r[i] = rgb[o];
        out_g[i] = rgb[o + 1];
        out_b[i] = rgb[o + 2];
    }
    return 0;
}

int cp9550_build_headers(
    enum cp9550_media media,
    enum cp9550_quality quality,
    int copies,
    int cut2x6,
    uint8_t *out,
    size_t out_cap)
{
    const cp9550_media_info *m = cp9550_media_get(media);
    uint8_t mode;
    uint8_t fine_deep;
    uint8_t cut;
    static const uint8_t hdr2_prefix[28] = {
        0x1B, 0x57, 0x21, 0x2E, 0x00, 0x80, 0x00, 0x22, 0x08, 0x03,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    };

    if (!m || !out || out_cap < 150)
        return -1;

    if (copies < 1)
        copies = 1;
    if (copies > 680)
        copies = 680;

    mode = (quality == CP9550_QUALITY_SUPERFINE) ? (uint8_t)0x80 : (uint8_t)0x00;
    fine_deep = (quality == CP9550_QUALITY_FINEDEEP) ? (uint8_t)0x01 : (uint8_t)0x00;
    cut = cut2x6 ? (uint8_t)0x83 : (uint8_t)0x00;

    memset(out, 0, 150);

    /* hdr1 */
    out[0] = 0x1B;
    out[1] = 0x57;
    out[2] = 0x20;
    out[3] = 0x2E;
    out[4] = 0x00;
    out[5] = 0x0A;
    out[6] = 0x10;
    write_be16(out + 14, (uint16_t)m->cols);
    write_be16(out + 16, (uint16_t)m->rows);

    /* hdr2 */
    memcpy(out + 50, hdr2_prefix, sizeof(hdr2_prefix));
    write_be16(out + 50 + 28, (uint16_t)copies);
    out[50 + 32] = cut;
    out[50 + 38] = mode;
    out[50 + 49] = 0x01;

    /* hdr3 */
    out[100] = 0x1B;
    out[101] = 0x57;
    out[102] = 0x22;
    out[103] = 0x2E;
    out[105] = 0x40;
    out[111] = fine_deep;

    return 150;
}

static size_t plane_block_size(size_t plane)
{
    return 12 + plane;
}

size_t cp9550_spool_size(enum cp9550_media media)
{
    const cp9550_media_info *m = cp9550_media_get(media);
    size_t plane;
    if (!m)
        return 0;
    plane = (size_t)m->cols * (size_t)m->rows;
    return 150 + 3 * plane_block_size(plane) + 4;
}

static void write_plane_block(uint8_t *dst, const uint8_t *plane, int cols, int rows)
{
    size_t plane_len = (size_t)cols * (size_t)rows;
    dst[0] = 0x1B;
    dst[1] = 0x5A;
    dst[2] = 0x54;
    dst[3] = 0x00;
    dst[4] = 0;
    dst[5] = 0;
    dst[6] = 0;
    dst[7] = 0;
    write_be16(dst + 8, (uint16_t)cols);
    write_be16(dst + 10, (uint16_t)rows);
    memcpy(dst + 12, plane, plane_len);
}

int cp9550_build_spool(
    const uint8_t *rgb,
    size_t rgb_len,
    enum cp9550_media media,
    enum cp9550_quality quality,
    int copies,
    uint8_t **out_spool,
    size_t *out_len)
{
    const cp9550_media_info *m = cp9550_media_get(media);
    size_t plane;
    size_t total;
    uint8_t *spool = NULL;
    uint8_t *r = NULL;
    uint8_t *g = NULL;
    uint8_t *b = NULL;
    size_t o;
    int hr;

    if (!m || !rgb || !out_spool || !out_len)
        return -1;

    *out_spool = NULL;
    *out_len = 0;

    plane = (size_t)m->cols * (size_t)m->rows;
    if (rgb_len < plane * 3)
        return -2;

    r = (uint8_t *)malloc(plane);
    g = (uint8_t *)malloc(plane);
    b = (uint8_t *)malloc(plane);
    if (!r || !g || !b) {
        hr = -3;
        goto done;
    }

    if (cp9550_split_planar_rgb(rgb, rgb_len, m->cols, m->rows, r, g, b) != 0) {
        hr = -2;
        goto done;
    }

    total = cp9550_spool_size(media);
    spool = (uint8_t *)malloc(total);
    if (!spool) {
        hr = -3;
        goto done;
    }

    if (cp9550_build_headers(media, quality, copies, 0, spool, 150) != 150) {
        hr = -4;
        goto done;
    }

    o = 150;
    write_plane_block(spool + o, r, m->cols, m->rows);
    o += plane_block_size(plane);
    write_plane_block(spool + o, g, m->cols, m->rows);
    o += plane_block_size(plane);
    write_plane_block(spool + o, b, m->cols, m->rows);
    o += plane_block_size(plane);
    memcpy(spool + o, kFooter9550S, 4);

    *out_spool = spool;
    *out_len = total;
    spool = NULL;
    hr = (int)total;

done:
    free(r);
    free(g);
    free(b);
    free(spool);
    return hr;
}

void cp9550_spool_free(void *p)
{
    free(p);
}
