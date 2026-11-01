#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <initguid.h>

#include "guid.h"
#include "filter.h"

#include <filterpipeline.h>
#include <new>

/* {C9550F17-06D3-4A55-9D77-C9550D0500F1} */
extern "C" const GUID CLSID_Cp9550RenderFilter =
    { 0xC9550F17, 0x06D3, 0x4A55, { 0x9D, 0x77, 0xC9, 0x55, 0x0D, 0x05, 0x00, 0xF1 } };

HINSTANCE g_hInstance = nullptr;

class Cp9550ClassFactory : public IClassFactory
{
public:
    Cp9550ClassFactory() : m_ref(1) {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        *ppv = nullptr;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory *>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return (ULONG)InterlockedIncrement(&m_ref);
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG c = InterlockedDecrement(&m_ref);
        if (c == 0)
            delete this;
        return (ULONG)c;
    }

    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown *outer, REFIID riid, void **ppv) override
    {
        if (outer)
            return CLASS_E_NOAGGREGATION;
        if (!ppv)
            return E_POINTER;
        *ppv = nullptr;

        Cp9550RenderFilter *filter = new (std::nothrow) Cp9550RenderFilter();
        if (!filter)
            return E_OUTOFMEMORY;
        HRESULT hr = filter->QueryInterface(riid, ppv);
        filter->Release();
        return hr;
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
    {
        if (lock)
            InterlockedIncrement(&Cp9550RenderFilter::s_objects);
        else
            InterlockedDecrement(&Cp9550RenderFilter::s_objects);
        return S_OK;
    }

private:
    LONG m_ref;
};

BOOL APIENTRY DllMain(HINSTANCE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        g_hInstance = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return Cp9550RenderFilter::s_objects == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID *ppv)
{
    if (!ppv)
        return E_POINTER;
    *ppv = nullptr;
    if (rclsid != CLSID_Cp9550RenderFilter)
        return CLASS_E_CLASSNOTAVAILABLE;

    Cp9550ClassFactory *factory = new (std::nothrow) Cp9550ClassFactory();
    if (!factory)
        return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}
