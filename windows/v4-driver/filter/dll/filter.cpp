#include "filter.h"
#include "omconvert.h"
#include "streamutil.h"
#include "cp9550_spool.h"

#include <msopc.h>
#include <xpsobjectmodel.h>
#include <oleauto.h>
#include <vector>
#include <stdio.h>

LONG Cp9550RenderFilter::s_objects = 0;

/* Lab breadcrumb: spoolsv (SYSTEM) and interactive user share this file. */
static void FilterLog(const char *msg, HRESULT hr = S_OK)
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

Cp9550RenderFilter::Cp9550RenderFilter()
    : m_ref(1)
    , m_reader(nullptr)
    , m_writer(nullptr)
    , m_bag(nullptr)
    , m_control(nullptr)
    , m_rasFactory(nullptr)
    , m_rasFactory2(nullptr)
    , m_shutdown(0)
    , m_cols(2152)
    , m_rows(1416)
    , m_quality(CP9550_QUALITY_SUPERFINE)
    , m_copies(1)
    , m_pagesDone(0)
{
    InterlockedIncrement(&s_objects);
}

Cp9550RenderFilter::~Cp9550RenderFilter()
{
    if (m_rasFactory2) { m_rasFactory2->Release(); m_rasFactory2 = nullptr; }
    if (m_rasFactory) { m_rasFactory->Release(); m_rasFactory = nullptr; }
    if (m_reader) { m_reader->Release(); m_reader = nullptr; }
    if (m_writer) { m_writer->Release(); m_writer = nullptr; }
    if (m_bag) { m_bag->Release(); m_bag = nullptr; }
    if (m_control) { m_control->Release(); m_control = nullptr; }
    InterlockedDecrement(&s_objects);
}

HRESULT STDMETHODCALLTYPE Cp9550RenderFilter::QueryInterface(REFIID riid, void **ppv)
{
    if (!ppv)
        return E_POINTER;
    *ppv = nullptr;
    if (riid == IID_IUnknown || riid == IID_IPrintPipelineFilter) {
        *ppv = static_cast<IPrintPipelineFilter *>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE Cp9550RenderFilter::AddRef()
{
    return (ULONG)InterlockedIncrement(&m_ref);
}

ULONG STDMETHODCALLTYPE Cp9550RenderFilter::Release()
{
    LONG c = InterlockedDecrement(&m_ref);
    if (c == 0)
        delete this;
    return (ULONG)c;
}

HRESULT STDMETHODCALLTYPE Cp9550RenderFilter::InitializeFilter(
    IInterFilterCommunicator *pINegotiation,
    IPrintPipelinePropertyBag *pIPropertyBag,
    IPrintPipelineManagerControl *pIPipelineControl)
{
    FilterLog("InitializeFilter enter");
    if (!pINegotiation || !pIPropertyBag || !pIPipelineControl)
        return E_POINTER;

    HRESULT hr = pINegotiation->RequestReader(reinterpret_cast<void **>(&m_reader));
    FilterLog("RequestReader", hr);
    if (FAILED(hr))
        return hr;
    hr = pINegotiation->RequestWriter(reinterpret_cast<void **>(&m_writer));
    FilterLog("RequestWriter", hr);
    if (FAILED(hr))
        return hr;

    m_bag = pIPropertyBag;
    m_bag->AddRef();
    m_control = pIPipelineControl;
    m_control->AddRef();
    FilterLog("InitializeFilter ok");
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Cp9550RenderFilter::ShutdownOperation()
{
    InterlockedExchange(&m_shutdown, 1);
    return S_OK;
}

HRESULT Cp9550RenderFilter::ResolveRasterFactory()
{
    if (m_rasFactory)
        return S_OK;
    if (!m_bag)
        return E_UNEXPECTED;

    VARIANT var;
    VariantInit(&var);
    HRESULT hr = m_bag->GetProperty(L"MS_IXpsRasterizationFactory", &var);
    FilterLog("GetProperty MS_IXpsRasterizationFactory", hr);
    if (FAILED(hr))
        return hr;
    if (var.vt != VT_UNKNOWN || !var.punkVal) {
        FilterLog("raster factory VT unexpected", E_NOINTERFACE);
        VariantClear(&var);
        return E_NOINTERFACE;
    }

    hr = var.punkVal->QueryInterface(IID_PPV_ARGS(&m_rasFactory));
    FilterLog("QI IXpsRasterizationFactory", hr);
    if (SUCCEEDED(hr))
        var.punkVal->QueryInterface(IID_PPV_ARGS(&m_rasFactory2));
    VariantClear(&var);
    return hr;
}

HRESULT Cp9550RenderFilter::ParseJobTicket(IFixedPage *page)
{
    /* Phase 1: keep 4×6 SuperFine defaults. Light-touch PT scrape for quality/copies. */
    m_cols = 2152;
    m_rows = 1416;
    m_quality = CP9550_QUALITY_SUPERFINE;
    m_copies = 1;

    if (!page)
        return S_OK;

    IPartPrintTicket *pt = nullptr;
    if (FAILED(page->GetPrintTicket(&pt)) || !pt)
        return S_OK;

    IPrintReadStream *stream = nullptr;
    if (SUCCEEDED(pt->GetStream(&stream)) && stream) {
        IStream *is = nullptr;
        if (SUCCEEDED(PrintReadStreamToIStream(stream, &is)) && is) {
            STATSTG st = {};
            if (SUCCEEDED(is->Stat(&st, STATFLAG_NONAME)) && st.cbSize.QuadPart > 0 && st.cbSize.QuadPart < 2 * 1024 * 1024) {
                std::vector<char> xml((size_t)st.cbSize.QuadPart + 1);
                ULONG read = 0;
                if (SUCCEEDED(is->Read(xml.data(), (ULONG)st.cbSize.QuadPart, &read))) {
                    xml[read] = 0;
                    if (strstr(xml.data(), "Photographic") || strstr(xml.data(), "FineDeep") || strstr(xml.data(), "finedeep"))
                        m_quality = CP9550_QUALITY_FINEDEEP;
                    else if (strstr(xml.data(), "\"Normal\"") || strstr(xml.data(), ">Normal<") || strstr(xml.data(), "PageOutputQuality\">Normal"))
                        m_quality = CP9550_QUALITY_FINE;
                    else if (strstr(xml.data(), "High") || strstr(xml.data(), "SuperFine"))
                        m_quality = CP9550_QUALITY_SUPERFINE;

                    const char *copiesKey = strstr(xml.data(), "JobCopiesAllDocuments");
                    if (copiesKey) {
                        const char *v = strstr(copiesKey, ">");
                        if (v) {
                            int c = atoi(v + 1);
                            if (c >= 1 && c <= 680)
                                m_copies = c;
                        }
                    }
                }
            }
            is->Release();
        }
        stream->Release();
    }
    pt->Release();
    return S_OK;
}

HRESULT Cp9550RenderFilter::ProcessFixedPage(IFixedPage *fixedPage)
{
    if (m_pagesDone > 0) {
        /* Phase 1: single page only — ignore further pages. */
        return S_OK;
    }

    HRESULT hr = ParseJobTicket(fixedPage);
    if (FAILED(hr))
        return hr;

    hr = ResolveRasterFactory();
    if (FAILED(hr))
        return hr;

    IXpsOMObjectFactory *omFactory = nullptr;
    hr = CoCreateInstance(CLSID_XpsOMObjectFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&omFactory));
    FilterLog("CoCreate XpsOMObjectFactory", hr);
    if (FAILED(hr))
        return hr;

    IOpcFactory *opcFactory = nullptr;
    hr = CoCreateInstance(CLSID_OpcFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&opcFactory));
    FilterLog("CoCreate OpcFactory", hr);
    if (FAILED(hr)) {
        omFactory->Release();
        return hr;
    }

    IXpsOMPage *omPage = nullptr;
    hr = FixedPageToXpsOMPage(fixedPage, omFactory, opcFactory, &omPage);
    FilterLog("FixedPageToXpsOMPage", hr);
    opcFactory->Release();
    omFactory->Release();
    if (FAILED(hr))
        return hr;

    XPS_SIZE pageSize = {};
    hr = omPage->GetPageDimensions(&pageSize);
    if (FAILED(hr) || pageSize.width <= 0 || pageSize.height <= 0) {
        FilterLog("GetPageDimensions", FAILED(hr) ? hr : E_UNEXPECTED);
        omPage->Release();
        return FAILED(hr) ? hr : E_UNEXPECTED;
    }

    /* XPS page size is in 1/96\". Target exact dye-sub pixels. */
    const FLOAT dpiX = (FLOAT)m_cols * 96.0f / pageSize.width;
    const FLOAT dpiY = (FLOAT)m_rows * 96.0f / pageSize.height;

    IXpsRasterizer *rasterizer = nullptr;
    if (m_rasFactory2) {
        hr = m_rasFactory2->CreateRasterizer(
            omPage,
            dpiX,
            dpiY,
            XPSRAS_RENDERING_MODE_ANTIALIASED,
            XPSRAS_RENDERING_MODE_ANTIALIASED,
            XPSRAS_PIXEL_FORMAT_32BPP_PBGRA_UINT_SRGB,
            XPSRAS_BACKGROUND_COLOR_OPAQUE,
            &rasterizer);
        FilterLog("CreateRasterizer2", hr);
    } else {
        hr = m_rasFactory->CreateRasterizer(
            omPage,
            (dpiX + dpiY) * 0.5f,
            XPSRAS_RENDERING_MODE_ANTIALIASED,
            XPSRAS_RENDERING_MODE_ANTIALIASED,
            &rasterizer);
        FilterLog("CreateRasterizer", hr);
    }
    omPage->Release();
    if (FAILED(hr))
        return hr;

    IWICBitmap *bitmap = nullptr;
    hr = rasterizer->RasterizeRect(0, 0, m_cols, m_rows, nullptr, &bitmap);
    FilterLog("RasterizeRect", hr);
    rasterizer->Release();
    if (FAILED(hr))
        return hr;

    BYTE *rgb = nullptr;
    size_t rgbLen = 0;
    hr = WicBitmapToPackedRgb(bitmap, (UINT)m_cols, (UINT)m_rows, &rgb, &rgbLen);
    FilterLog("WicBitmapToPackedRgb", hr);
    bitmap->Release();
    if (FAILED(hr))
        return hr;

    uint8_t *spool = nullptr;
    size_t spoolLen = 0;
    int built = cp9550_build_spool(
        rgb,
        rgbLen,
        CP9550_MEDIA_4X6,
        (enum cp9550_quality)m_quality,
        m_copies,
        &spool,
        &spoolLen);
    CoTaskMemFree(rgb);
    if (built < 0) {
        FilterLog("cp9550_build_spool", E_FAIL);
        return E_FAIL;
    }

    hr = WriteAllBytes(m_writer, spool, (ULONG)spoolLen);
    FilterLog("WriteAllBytes", hr);
    cp9550_spool_free(spool);
    if (FAILED(hr))
        return hr;

    m_pagesDone++;
    return S_OK;
}

HRESULT STDMETHODCALLTYPE Cp9550RenderFilter::StartOperation()
{
    HRESULT hr = S_OK;
    FilterLog("StartOperation enter");

    if (!m_reader || !m_writer)
        return E_UNEXPECTED;

    while (!InterlockedCompareExchange(&m_shutdown, 0, 0)) {
        IUnknown *part = nullptr;
        hr = m_reader->GetXpsPart(&part);
        if (FAILED(hr)) {
            FilterLog("GetXpsPart", hr);
            break;
        }
        if (!part) {
            hr = S_OK;
            FilterLog("GetXpsPart end");
            break; /* end of document */
        }

        IFixedPage *page = nullptr;
        if (SUCCEEDED(part->QueryInterface(IID_PPV_ARGS(&page)))) {
            FilterLog("ProcessFixedPage begin");
            hr = ProcessFixedPage(page);
            FilterLog("ProcessFixedPage done", hr);
            page->Release();
            part->Release();
            if (FAILED(hr))
                break;
            continue;
        }

        /* Consume non-page parts (document sequence, documents, etc.). */
        FilterLog("skip non-page part");
        part->Release();
    }

    if (m_writer)
        m_writer->Close();

    if (FAILED(hr) && m_control)
        m_control->RequestShutdown(hr, nullptr);
    if (m_control)
        m_control->FilterFinished();

    FilterLog("StartOperation exit", hr);
    return hr;
}
