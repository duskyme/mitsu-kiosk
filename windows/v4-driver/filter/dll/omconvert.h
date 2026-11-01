#pragma once

#include <filterpipeline.h>
#include <xpsobjectmodel.h>

HRESULT FixedPageToXpsOMPage(
    _In_ IFixedPage *fixedPage,
    _In_ IXpsOMObjectFactory *factory,
    _In_ IOpcFactory *opcFactory,
    _Outptr_ IXpsOMPage **ppPage);
