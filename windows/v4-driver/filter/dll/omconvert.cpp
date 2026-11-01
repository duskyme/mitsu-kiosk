#include "omconvert.h"
#include "streamutil.h"

#include <msopc.h>
#include <string.h>

static XPS_IMAGE_TYPE ImageTypeFromContentType(const wchar_t *ct)
{
    if (!ct)
        return XPS_IMAGE_TYPE_PNG;
    if (wcsstr(ct, L"jpeg") || wcsstr(ct, L"jpg"))
        return XPS_IMAGE_TYPE_JPEG;
    if (wcsstr(ct, L"tiff") || wcsstr(ct, L"tif"))
        return XPS_IMAGE_TYPE_TIFF;
    if (wcsstr(ct, L"wdp") || wcsstr(ct, L"hdp"))
        return XPS_IMAGE_TYPE_WDP;
    if (wcsstr(ct, L"jxr"))
        return XPS_IMAGE_TYPE_JXR;
    return XPS_IMAGE_TYPE_PNG;
}

static HRESULT AddPartResources(
    IFixedPage *fixedPage,
    IXpsOMObjectFactory *factory,
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

    for (; !it->IsDone(); it->Next()) {
        BSTR uri = nullptr;
        IUnknown *part = nullptr;
        hr = it->Current(&uri, &part);
        if (FAILED(hr))
            break;

        IPartImage *img = nullptr;
        IPartFont *font = nullptr;
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
            IXpsOMFontResource *res = nullptr;
            if (SUCCEEDED(partHr)) {
                partHr = factory->CreateFontResource(
                    stream,
                    XPS_FONT_EMBEDDING_NORMAL,
                    partUri,
                    FALSE,
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
        }

        if (uri) SysFreeString(uri);
        part->Release();
        if (FAILED(partHr)) {
            hr = partHr;
            break;
        }
    }

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

    IXpsOMPartResources *resources = nullptr;
    HRESULT hr = factory->CreatePartResources(&resources);
    if (FAILED(hr))
        return hr;

    hr = AddPartResources(fixedPage, factory, resources);
    if (FAILED(hr)) {
        resources->Release();
        return hr;
    }

    BSTR pageUri = nullptr;
    hr = fixedPage->GetUri(&pageUri);
    if (FAILED(hr)) {
        resources->Release();
        return hr;
    }

    IPrintReadStream *pageStream = nullptr;
    hr = fixedPage->GetStream(&pageStream);
    if (FAILED(hr)) {
        SysFreeString(pageUri);
        resources->Release();
        return hr;
    }

    IStream *istream = nullptr;
    hr = PrintReadStreamToIStream(pageStream, &istream);
    pageStream->Release();
    if (FAILED(hr)) {
        SysFreeString(pageUri);
        resources->Release();
        return hr;
    }

    IOpcPartUri *partUri = nullptr;
    hr = factory->CreatePartUri(pageUri, &partUri);
    SysFreeString(pageUri);
    if (FAILED(hr)) {
        istream->Release();
        resources->Release();
        return hr;
    }

    IXpsOMPage *page = nullptr;
    hr = factory->CreatePageFromStream(istream, partUri, resources, FALSE, &page);
    partUri->Release();
    istream->Release();
    resources->Release();
    if (FAILED(hr))
        return hr;

    *ppPage = page;
    return S_OK;
}
