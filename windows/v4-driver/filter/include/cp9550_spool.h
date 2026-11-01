/* CP-9550 planar RGB spool builder — C oracle matching Cp9550Native.Core Protocol.BuildSpool.
 * Planar order is R then G then B (MK-4). */
#pragma once

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifdef CP9550_SPOOL_EXPORTS
#define CP9550_SPOOL_API __declspec(dllexport)
#elif defined(CP9550_SPOOL_DLL)
#define CP9550_SPOOL_API __declspec(dllimport)
#else
#define CP9550_SPOOL_API
#endif

enum cp9550_quality {
    CP9550_QUALITY_FINE = 0,
    CP9550_QUALITY_SUPERFINE = 1,
    CP9550_QUALITY_FINEDEEP = 2,
};

enum cp9550_media {
    CP9550_MEDIA_4X6 = 0, /* 2152×1416, media_code 0x02 */
    CP9550_MEDIA_5X7 = 1, /* 1812×2452, media_code 0x04 */
    CP9550_MEDIA_6X9 = 2, /* 2152×2792, media_code 0x05 */
};

typedef struct cp9550_media_info {
    const char *key;
    int media_code;
    int cols;
    int rows;
} cp9550_media_info;

CP9550_SPOOL_API const cp9550_media_info *cp9550_media_get(enum cp9550_media media);

/* Packed RGB → planar R,G,B. out_r/g/b must hold cols*rows bytes each. */
CP9550_SPOOL_API int cp9550_split_planar_rgb(
    const uint8_t *rgb,
    size_t rgb_len,
    int cols,
    int rows,
    uint8_t *out_r,
    uint8_t *out_g,
    uint8_t *out_b);

/* Headers only (150 bytes). out must be ≥150. Returns bytes written or <0. */
CP9550_SPOOL_API int cp9550_build_headers(
    enum cp9550_media media,
    enum cp9550_quality quality,
    int copies,
    int cut2x6,
    uint8_t *out,
    size_t out_cap);

/* Full spool: headers + plane blocks + footer.
 * *out_spool is malloc'd; caller frees with cp9550_spool_free.
 * Returns byte length or <0 on error. */
CP9550_SPOOL_API int cp9550_build_spool(
    const uint8_t *rgb,
    size_t rgb_len,
    enum cp9550_media media,
    enum cp9550_quality quality,
    int copies,
    uint8_t **out_spool,
    size_t *out_len);

CP9550_SPOOL_API void cp9550_spool_free(void *p);

/* Expected spool size for media (no RGB needed). */
CP9550_SPOOL_API size_t cp9550_spool_size(enum cp9550_media media);

#ifdef __cplusplus
}
#endif
