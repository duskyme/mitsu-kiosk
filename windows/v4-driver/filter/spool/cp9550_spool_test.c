/* Self-check: layout matches Cp9550Native.Core ProtocolTests expectations. */
#include "cp9550_spool.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int fail(const char *msg)
{
    fprintf(stderr, "FAIL: %s\n", msg);
    return 1;
}

int main(void)
{
    const cp9550_media_info *m = cp9550_media_get(CP9550_MEDIA_4X6);
    size_t plane;
    uint8_t *rgb = NULL;
    uint8_t *spool = NULL;
    size_t spool_len = 0;
    size_t i;
    uint8_t hdr[150];
    uint8_t r1, g1, b1;
    int rc = 1;

    if (!m || m->cols != 2152 || m->rows != 1416)
        return fail("4x6 media dims");

    if (cp9550_build_headers(CP9550_MEDIA_4X6, CP9550_QUALITY_SUPERFINE, 2, 0, hdr, sizeof(hdr)) != 150)
        return fail("headers length");
    if (hdr[0] != 0x1B || hdr[1] != 0x57 || hdr[2] != 0x20 || hdr[3] != 0x2E)
        return fail("hdr1 magic");
    if (hdr[14] != 0x08 || hdr[15] != 0x68) /* 2152 */
        return fail("cols be16");
    if (hdr[16] != 0x05 || hdr[17] != 0x88) /* 1416 */
        return fail("rows be16");
    if (hdr[50 + 38] != 0x80)
        return fail("superfine mode");
    if (hdr[50 + 28] != 0x00 || hdr[50 + 29] != 0x02)
        return fail("copies");

    plane = (size_t)m->cols * (size_t)m->rows;
    rgb = (uint8_t *)malloc(plane * 3);
    if (!rgb)
        return fail("oom rgb");
    for (i = 0; i < plane; i++) {
        rgb[i * 3] = 10;
        rgb[i * 3 + 1] = 20;
        rgb[i * 3 + 2] = 30;
    }

    if (cp9550_build_spool(rgb, plane * 3, CP9550_MEDIA_4X6, CP9550_QUALITY_FINE, 1, &spool, &spool_len) < 0)
        return fail("build_spool");

    if (spool_len != 150 + 3 * (12 + plane) + 4)
        return fail("spool size");
    if (memcmp(spool + spool_len - 4, "\x1B\x50\x47\x00", 4) != 0)
        return fail("footer");
    if (memcmp(spool + 150, "\x1B\x5A\x54\x00", 4) != 0)
        return fail("plane magic");

    r1 = spool[150 + 12];
    g1 = spool[150 + 12 + plane + 12];
    b1 = spool[150 + 12 + 2 * (plane + 12)];
    if (r1 != 10 || g1 != 20 || b1 != 30)
        return fail("planar RGB order (R then G then B)");

    {
        uint8_t tiny_rgb[3] = { 10, 20, 30 };
        uint8_t pr, pg, pb;
        if (cp9550_split_planar_rgb(tiny_rgb, 3, 1, 1, &pr, &pg, &pb) != 0)
            return fail("split");
        if (pr != 10 || pg != 20 || pb != 30)
            return fail("split channels");
    }

    printf("PASS cp9550_spool_test (4x6 spool %zu bytes, planar RGB OK)\n", spool_len);
    rc = 0;

    cp9550_spool_free(spool);
    free(rgb);
    return rc;
}
