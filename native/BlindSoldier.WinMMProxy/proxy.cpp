#include "proxy_state.h"

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        blind_soldier::InitializeWinmmProxy(instance);
    }
    return TRUE;
}

#if !defined(_M_IX86)
#error Blind Soldier WinMM proxy must be built for x86.
#endif

#define BS_WINMM_FORWARD(stub, index)               \
extern "C" __declspec(naked) void stub() {          \
    __asm { pushfd }                                 \
    __asm { pushad }                                 \
    __asm { call EnsureWinmmAndBootstrapReady }      \
    __asm { popad }                                  \
    __asm { popfd }                                  \
    __asm { jmp dword ptr [g_winmmExports + index * 4] } \
}

#include "winmm_exports.inc"

#undef BS_WINMM_FORWARD
