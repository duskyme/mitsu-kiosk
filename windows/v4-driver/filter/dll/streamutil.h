#pragma once

#include <filterpipeline.h>
#include <objidl.h>
#include <wincodec.h>

HRESULT PrintReadStreamToIStream(_In_ IPrintReadStream *src, _Outptr_ IStream **ppStream);
HRESULT WicBitmapToPackedRgb(
    _In_ IWICBitmap *bitmap,
    UINT expectCols,
    UINT expectRows,
    _Outptr_result_bytebuffer_(*pcb) BYTE **ppRgb,
    _Out_ size_t *pcb);
HRESULT WriteAllBytes(_In_ IPrintWriteStream *writer, _In_reads_bytes_(cb) const void *data, ULONG cb);
