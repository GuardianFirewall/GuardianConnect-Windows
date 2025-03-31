#pragma once
#include <cstddef>

#include "NativeRoutines.h"

class ScopedHeapAlloc {
public:
    explicit ScopedHeapAlloc(SIZE_T dw_bytes) {
        lp_alloc_mem_ = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dw_bytes);
    }

    ~ScopedHeapAlloc() {
        if (lp_alloc_mem_) {
            HeapFree(GetProcessHeap(), 0, lp_alloc_mem_);
        }
    }

    ScopedHeapAlloc(const ScopedHeapAlloc&) = delete;

    ScopedHeapAlloc& operator=(const ScopedHeapAlloc&) = delete;

    LPVOID lp_alloc_mem() { return lp_alloc_mem_; }

private:
    LPVOID lp_alloc_mem_ = NULL;
};