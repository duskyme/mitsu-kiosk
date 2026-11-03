#pragma once

#include <filterpipeline.h>
#include <xpsrassvc.h>
#include "cp9550_spool.h"

class Cp9550RenderFilter : public IPrintPipelineFilter
{
public:
    Cp9550RenderFilter();
    virtual ~Cp9550RenderFilter();

    /* IUnknown */
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void **ppv) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;

    /* IPrintPipelineFilter */
    HRESULT STDMETHODCALLTYPE InitializeFilter(
        IInterFilterCommunicator *pINegotiation,
        IPrintPipelinePropertyBag *pIPropertyBag,
        IPrintPipelineManagerControl *pIPipelineControl) override;
    HRESULT STDMETHODCALLTYPE ShutdownOperation() override;
    HRESULT STDMETHODCALLTYPE StartOperation() override;

    static LONG s_objects;

private:
    HRESULT ProcessFixedPage(IFixedPage *page);
    HRESULT ResolveRasterFactory();
    HRESULT ParseJobTicket(IFixedPage *page);
    void ApplyMedia(enum cp9550_media media);

    LONG m_ref;
    IXpsDocumentProvider *m_reader;
    IPrintWriteStream *m_writer;
    IPrintPipelinePropertyBag *m_bag;
    IPrintPipelineManagerControl *m_control;
    IXpsRasterizationFactory *m_rasFactory;
    IXpsRasterizationFactory2 *m_rasFactory2;
    volatile LONG m_shutdown;

    /* Defaults: 4×6 SuperFine, 1 copy — overridden from PrintTicket */
    int m_cols;
    int m_rows;
    int m_media;   /* cp9550_media */
    int m_quality; /* cp9550_quality */
    int m_copies;
    int m_pagesDone;
};
