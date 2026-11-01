#include "streamutil.h"

#include <shlwapi.h>
#include <vector>

HRESULT PrintReadStreamToIStream(_In_ IPrintReadStream *src, _Outptr_ IStream **ppStream)
{
    *ppStream = nullptr;
    if (!src)
        return E_POINTER;

    std::vector<BYTE> buf;
    buf.reserve(256 * 1024);

    for (;;) {
        BYTE chunk[64 * 1024];
        ULONG read = 0;
        BOOL eof = FALSE;
        HRESULT hr = src->ReadBytes(chunk, sizeof(chunk), &read, &eof);
        if (FAILED(hr))
            return hr;
        if (read)
            buf.insert(buf.end(), chunk, chunk + read);
        if (eof || read == 0)
            break;
    }

    IStream *mem = SHCreateMemStream(buf.empty() ? nullptr : buf.data(), (UINT)buf.size());
    if (!mem)
        return E_OUTOFMEMORY;
    *ppStream = mem;
    return S_OK;
}

HRESULT WicBitmapToPackedRgb(
    _In_ IWICBitmap *bitmap,
    UINT expectCols,
    UINT expectRows,
    _Outptr_result_bytebuffer_(*pcb) BYTE **ppRgb,
    _Out_ size_t *pcb)
{
    *ppRgb = nullptr;
    *pcb = 0;
    if (!bitmap)
        return E_POINTER;

    UINT w = 0, h = 0;
    HRESULT hr = bitmap->GetSize(&w, &h);
    if (FAILED(hr))
        return hr;
    if (w != expectCols || h != expectRows)
        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);

    IWICImagingFactory *factory = nullptr;
    hr = CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&factory));
    if (FAILED(hr))
        return hr;

    IWICFormatConverter *conv = nullptr;
    hr = factory->CreateFormatConverter(&conv);
    if (FAILED(hr)) {
        factory->Release();
        return hr;
    }

    hr = conv->Initialize(
        bitmap,
        GUID_WICPixelFormat32bppBGRA,
        WICBitmapDitherTypeNone,
        nullptr,
        0.0,
        WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) {
        conv->Release();
        factory->Release();
        return hr;
    }

    const size_t plane = (size_t)w * (size_t)h;
    const size_t strideSrc = (size_t)w * 4;
    std::vector<BYTE> bgra(plane * 4);
    WICRect rect = { 0, 0, (INT)w, (INT)h };
    hr = conv->CopyPixels(&rect, (UINT)strideSrc, (UINT)bgra.size(), bgra.data());
    conv->Release();
    factory->Release();
    if (FAILED(hr))
        return hr;

    BYTE *rgb = (BYTE *)CoTaskMemAlloc(plane * 3);
    if (!rgb)
        return E_OUTOFMEMORY;

    for (size_t i = 0; i < plane; i++) {
        const BYTE *p = &bgra[i * 4];
        rgb[i * 3 + 0] = p[2]; /* R */
        rgb[i * 3 + 1] = p[1]; /* G */
        rgb[i * 3 + 2] = p[0]; /* B */
    }

    *ppRgb = rgb;
    *pcb = plane * 3;
    return S_OK;
}

HRESULT WriteAllBytes(_In_ IPrintWriteStream *writer, _In_reads_bytes_(cb) const void *data, ULONG cb)
{
    if (!writer || (!data && cb))
        return E_POINTER;
    const BYTE *p = (const BYTE *)data;
    ULONG left = cb;
    while (left) {
        ULONG wrote = 0;
        HRESULT hr = writer->WriteBytes(p, left, &wrote);
        if (FAILED(hr))
            return hr;
        if (wrote == 0)
            return E_FAIL;
        p += wrote;
        left -= wrote;
    }
    return S_OK;
}
