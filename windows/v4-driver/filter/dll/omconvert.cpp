#include "omconvert.h"
#include "streamutil.h"

#include <msopc.h>
#include <xpsobjectmodel_1.h>
#include <string.h>
#include <stdio.h>

/* Same breadcrumb file as filter.cpp — spoolsv/printfilterpipelinesvc (SYSTEM). */
static void OmLog(const char *msg, HRESULT hr = S_OK)
{
    CreateDirectoryW(L"C:\\ProgramData\\CP9550", nullptr);
    CreateDirectoryW(L"C:\\ProgramData\\CP9550\\log", nullptr);
    HANDLE h = CreateFileW(L"C:\\ProgramData\\CP9550\\log\\filter.log",
                           FILE_APPEND_DATA, FILE_SHARE_READ, nullptr,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE)
        return;
    char line[512];
    SYSTEMTIME st = {};
    GetLocalTime(&st);
    int n = sprintf_s(line, "[%02u:%02u:%02u] %s hr=0x%08X\r\n",
                      st.wHour, st.wMinute, st.wSecond, msg, (unsigned)hr);
    if (n > 0) {
        DWORD written = 0;
        WriteFile(h, line, (DWORD)n, &written, nullptr);
    }
    CloseHandle(h);
}

static XPS_IMAGE_TYPE ImageTypeFromContentType(const wchar_t *ct)
{
    if (!ct)
        return XPS_IMAGE_TYPE_PNG;
    if (wcsstr(ct, L"jpeg") || wcsstr(ct, L"jpg"))
        return XPS_IMAGE_TYPE_JPEG;
    if (wcsstr(ct, L"tiff") || wcsstr(ct, L"tif"))
        return XPS_IMAGE_TYPE_TIFF;
    if (wcsstr(ct, L"wdp") || wcsstr(ct, L"hdp") || wcsstr(ct, L"ms-photo"))
        return XPS_IMAGE_TYPE_WDP;
    if (wcsstr(ct, L"jxr") || wcsstr(ct, L"vnd.ms-photo"))
        return XPS_IMAGE_TYPE_JXR;
    return XPS_IMAGE_TYPE_PNG;
}

static bool UriLooksObfuscated(const wchar_t *uri)
{
    if (!uri)
        return false;
    /* Restricted fonts ship as *.odttf (GUID-named) in both MSXPS and OpenXPS. */
    size_t n = wcslen(uri);
    if (n < 6)
        return false;
    const wchar_t *ext = uri + (n - 6);
    return _wcsicmp(ext, L".odttf") == 0;
}

static HRESULT AddPartResources(
    IFixedPage *fixedPage,
    IXpsOMObjectFactory *factory,
    IXpsOMObjectFactory1 *factory1,
    IXpsOMPartResources *resources)
{
    IXpsPartIterator *it = nullptr;
    HRESULT hr = fixedPage->GetXpsPartIterator(&it);
    if (FAILED(hr))
        return hr;

    IXpsOMImageResourceCollection *images = nullptr;
    IXpsOMFontResourceCollection *fonts = nullptr;
    hr = resources->GetImageResources(&images);
    if (SUCCEEDED(hr))
        hr = resources->GetFontResources(&fonts);
    if (FAILED(hr)) {
        if (images) images->Release();
        it->Release();
        return hr;
    }

    IXpsOMRemoteDictionaryResourceCollection *dicts = nullptr;
    /* Optional — not all pages use remote dictionaries. */
    (void)resources->GetRemoteDictionaryResources(&dicts);

    for (; !it->IsDone(); it->Next()) {
        BSTR uri = nullptr;
        IUnknown *part = nullptr;
        hr = it->Current(&uri, &part);
        if (FAILED(hr))
            break;

        IPartImage *img = nullptr;
        IPartFont *font = nullptr;
        IPartResourceDictionary *resDict = nullptr;
        HRESULT partHr = S_OK;

        if (SUCCEEDED(part->QueryInterface(IID_PPV_ARGS(&img)))) {
            IPrintReadStream *prs = nullptr;
            BSTR contentType = nullptr;
            partHr = img->GetStream(&prs);
            if (SUCCEEDED(partHr))
                partHr = img->GetImageProperties(&contentType);
            IStream *stream = nullptr;
            if (SUCCEEDED(partHr))
                partHr = PrintReadStreamToIStream(prs, &stream);
            IOpcPartUri *partUri = nullptr;
            if (SUCCEEDED(partHr))
                partHr = factory->CreatePartUri(uri, &partUri);
            IXpsOMImageResource *res = nullptr;
            if (SUCCEEDED(partHr)) {
                partHr = factory->CreateImageResource(
                    stream,
                    ImageTypeFromContentType(contentType),
                    partUri,
                    &res);
            }
            if (SUCCEEDED(partHr)) {
                partHr = images->Append(res);
                res->Release();
            }
            if (partUri) partUri->Release();
            if (stream) stream->Release();
            if (contentType) SysFreeString(contentType);
            if (prs) prs->Release();
            img->Release();
        } else if (SUCCEEDED(part->QueryInterface(IID_PPV_ARGS(&font)))) {
            IPrintReadStream *prs = nullptr;
            partHr = font->GetStream(&prs);
            IStream *stream = nullptr;
            if (SUCCEEDED(partHr))
                partHr = PrintReadStreamToIStream(prs, &stream);
            IOpcPartUri *partUri = nullptr;
            if (SUCCEEDED(partHr))
                partHr = factory->CreatePartUri(uri, &partUri);
            bool obfuscated = UriLooksObfuscated(uri);
            IPartFont2 *font2 = nullptr;
            if (SUCCEEDED(font->QueryInterface(IID_PPV_ARGS(&font2)))) {
                /* IPartFont2::GetFontProperties can confirm restricted/obfuscated fonts. */
                EXpsFontRestriction restriction = (EXpsFontRestriction)0;
                if (SUCCEEDED(font2->GetFontRestriction(&restriction)) && restriction != 0)
                    obfuscated = true;
                font2->Release();
            }
            IXpsOMFontResource *res = nullptr;
            if (SUCCEEDED(partHr)) {
                partHr = factory->CreateFontResource(
                    stream,
                    obfuscated ? XPS_FONT_EMBEDDING_OBFUSCATED : XPS_FONT_EMBEDDING_NORMAL,
                    partUri,
                    obfuscated ? TRUE : FALSE,
                    &res);
            }
            if (SUCCEEDED(partHr)) {
                partHr = fonts->Append(res);
                res->Release();
            }
            if (partUri) partUri->Release();
            if (stream) stream->Release();
            if (prs) prs->Release();
            font->Release();
        } else if (dicts && SUCCEEDED(part->QueryInterface(IID_PPV_ARGS(&resDict)))) {
            IPrintReadStream *prs = nullptr;
            partHr = resDict->GetStream(&prs);
            IStream *stream = nullptr;
            if (SUCCEEDED(partHr))
                partHr = PrintReadStreamToIStream(prs, &stream);
            IOpcPartUri *partUri = nullptr;
            if (SUCCEEDED(partHr))
                partHr = factory->CreatePartUri(uri, &partUri);
            IXpsOMRemoteDictionaryResource *res = nullptr;
            if (SUCCEEDED(partHr)) {
                if (factory1) {
                    partHr = factory1->CreateRemoteDictionaryResourceFromStream1(
                        stream, partUri, resources, &res);
                } else {
                    partHr = factory->CreateRemoteDictionaryResourceFromStream(
                        stream, partUri, resources, &res);
                }
            }
            if (SUCCEEDED(partHr)) {
                partHr = dicts->Append(res);
                res->Release();
            }
            if (partUri) partUri->Release();
            if (stream) stream->Release();
            if (prs) prs->Release();
            resDict->Release();
        }

        if (uri) SysFreeString(uri);
        part->Release();
        if (FAILED(partHr)) {
            OmLog("AddPartResources part failed", partHr);
            hr = partHr;
            break;
        }
    }

    if (dicts) dicts->Release();
    fonts->Release();
    images->Release();
    it->Release();
    return SUCCEEDED(hr) ? S_OK : hr;
}

HRESULT FixedPageToXpsOMPage(
    _In_ IFixedPage *fixedPage,
    _In_ IXpsOMObjectFactory *factory,
    _In_ IOpcFactory * /*opcFactory*/,
    _Outptr_ IXpsOMPage **ppPage)
{
    *ppPage = nullptr;
    if (!fixedPage || !factory)
        return E_POINTER;

    /* OpenXPS pages need IXpsOMObjectFactory1::CreatePageFromStream1.
       Classic CreatePageFromStream on OpenXPS markup returns XPS_E_INVALID_MARKUP (0x8052000C). */
    IXpsOMObjectFactory1 *factory1 = nullptr;
    HRESULT hrQi = factory->QueryInterface(IID_PPV_ARGS(&factory1));
    OmLog(SUCCEEDED(hrQi) ? "QI IXpsOMObjectFactory1 ok" : "QI IXpsOMObjectFactory1 fail", hrQi);

    IXpsOMPartResources *resources = nullptr;
    HRESULT hr = factory->CreatePartResources(&resources);
    if (FAILED(hr)) {
        if (factory1) factory1->Release();
        return hr;
    }

    hr = AddPartResources(fixedPage, factory, factory1, resources);
    OmLog("AddPartResources", hr);
    if (FAILED(hr)) {
        resources->Release();
        if (factory1) factory1->Release();
        return hr;
    }

    BSTR pageUri = nullptr;
    hr = fixedPage->GetUri(&pageUri);
    if (FAILED(hr)) {
        resources->Release();
        if (factory1) factory1->Release();
        return hr;
    }

    IPrintReadStream *pageStream = nullptr;
    hr = fixedPage->GetStream(&pageStream);
    if (FAILED(hr)) {
        SysFreeString(pageUri);
        resources->Release();
        if (factory1) factory1->Release();
        return hr;
    }

    IStream *istream = nullptr;
    hr = PrintReadStreamToIStream(pageStream, &istream);
    pageStream->Release();
    if (FAILED(hr)) {
        SysFreeString(pageUri);
        resources->Release();
        if (factory1) factory1->Release();
        return hr;
    }

    /* Rewind — CreatePageFromStream* reads from current seek. */
    LARGE_INTEGER zero = {};
    istream->Seek(zero, STREAM_SEEK_SET, nullptr);

    IOpcPartUri *partUri = nullptr;
    hr = factory->CreatePartUri(pageUri, &partUri);
    SysFreeString(pageUri);
    if (FAILED(hr)) {
        istream->Release();
        resources->Release();
        if (factory1) factory1->Release();
        return hr;
    }

    IXpsOMPage *page = nullptr;
    if (factory1) {
        IXpsOMPage1 *page1 = nullptr;
        hr = factory1->CreatePageFromStream1(istream, partUri, resources, FALSE, &page1);
        OmLog("CreatePageFromStream1", hr);
        if (SUCCEEDED(hr) && page1) {
            hr = page1->QueryInterface(IID_PPV_ARGS(&page));
            OmLog("QI IXpsOMPage from Page1", hr);
            page1->Release();
        }
    } else {
        hr = factory->CreatePageFromStream(istream, partUri, resources, FALSE, &page);
        OmLog("CreatePageFromStream (classic)", hr);
    }

    partUri->Release();
    istream->Release();
    resources->Release();
    if (factory1) factory1->Release();
    if (FAILED(hr))
        return hr;

    *ppPage = page;
    return S_OK;
}
