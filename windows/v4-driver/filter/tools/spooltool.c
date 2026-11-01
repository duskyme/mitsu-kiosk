/* Offline bring-up: build a CP-9550 spool from solid RGB and optionally write a file.
 * Documented path when the XPS queue is not yet installed:
 *   spooltool --solid 220 40 40 --out job.bin
 *   then Cp9550SpoolBridge --spool-file job.bin  (or TCP to 127.0.0.1:9100)
 */
#include "cp9550_spool.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void usage(void)
{
    fprintf(stderr,
        "cp9550-spooltool — build planar RGB spool (Phase 1 offline path)\n"
        "  --solid R G B     fill 4x6 with packed RGB (0-255)\n"
        "  --quality fine|superfine|finedeep  (default superfine)\n"
        "  --copies N        (default 1)\n"
        "  --out PATH        write spool bytes\n"
        "  --stdout          write spool to stdout (binary)\n");
}

static int parse_quality(const char *s, enum cp9550_quality *q)
{
    if (_stricmp(s, "fine") == 0) {
        *q = CP9550_QUALITY_FINE;
        return 0;
    }
    if (_stricmp(s, "superfine") == 0) {
        *q = CP9550_QUALITY_SUPERFINE;
        return 0;
    }
    if (_stricmp(s, "finedeep") == 0) {
        *q = CP9550_QUALITY_FINEDEEP;
        return 0;
    }
    return -1;
}

int main(int argc, char **argv)
{
    int r = 200, g = 200, b = 200;
    int have_solid = 0;
    enum cp9550_quality quality = CP9550_QUALITY_SUPERFINE;
    int copies = 1;
    const char *out_path = NULL;
    int to_stdout = 0;
    const cp9550_media_info *m;
    size_t plane;
    uint8_t *rgb = NULL;
    uint8_t *spool = NULL;
    size_t spool_len = 0;
    size_t i;
    int ai;
    int rc = 1;

    for (ai = 1; ai < argc; ai++) {
        if (strcmp(argv[ai], "--help") == 0 || strcmp(argv[ai], "-h") == 0) {
            usage();
            return 0;
        }
        if (strcmp(argv[ai], "--solid") == 0 && ai + 3 < argc) {
            r = atoi(argv[++ai]);
            g = atoi(argv[++ai]);
            b = atoi(argv[++ai]);
            have_solid = 1;
            continue;
        }
        if (strcmp(argv[ai], "--quality") == 0 && ai + 1 < argc) {
            if (parse_quality(argv[++ai], &quality) != 0) {
                fprintf(stderr, "bad --quality\n");
                return 1;
            }
            continue;
        }
        if (strcmp(argv[ai], "--copies") == 0 && ai + 1 < argc) {
            copies = atoi(argv[++ai]);
            continue;
        }
        if (strcmp(argv[ai], "--out") == 0 && ai + 1 < argc) {
            out_path = argv[++ai];
            continue;
        }
        if (strcmp(argv[ai], "--stdout") == 0) {
            to_stdout = 1;
            continue;
        }
        fprintf(stderr, "unknown arg: %s\n", argv[ai]);
        usage();
        return 1;
    }

    if (!have_solid) {
        usage();
        return 1;
    }
    if (!out_path && !to_stdout) {
        fprintf(stderr, "need --out or --stdout\n");
        return 1;
    }

    m = cp9550_media_get(CP9550_MEDIA_4X6);
    plane = (size_t)m->cols * (size_t)m->rows;
    rgb = (uint8_t *)malloc(plane * 3);
    if (!rgb) {
        fprintf(stderr, "oom\n");
        return 1;
    }
    for (i = 0; i < plane; i++) {
        rgb[i * 3] = (uint8_t)r;
        rgb[i * 3 + 1] = (uint8_t)g;
        rgb[i * 3 + 2] = (uint8_t)b;
    }

    if (cp9550_build_spool(rgb, plane * 3, CP9550_MEDIA_4X6, quality, copies, &spool, &spool_len) < 0) {
        fprintf(stderr, "build_spool failed\n");
        goto done;
    }

    fprintf(stderr, "spool %zu bytes (4x6 planar RGB R=%d G=%d B=%d)\n", spool_len, r, g, b);

    if (out_path) {
        FILE *f = fopen(out_path, "wb");
        if (!f) {
            fprintf(stderr, "cannot write %s\n", out_path);
            goto done;
        }
        if (fwrite(spool, 1, spool_len, f) != spool_len) {
            fclose(f);
            fprintf(stderr, "short write\n");
            goto done;
        }
        fclose(f);
    }

    if (to_stdout) {
        if (fwrite(spool, 1, spool_len, stdout) != spool_len) {
            fprintf(stderr, "stdout write failed\n");
            goto done;
        }
    }

    rc = 0;
done:
    cp9550_spool_free(spool);
    free(rgb);
    return rc;
}
