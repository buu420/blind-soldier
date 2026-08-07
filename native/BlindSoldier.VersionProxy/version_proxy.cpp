#include "../BlindSoldier.WinMMProxy/proxy_state.h"
#include "app_loader_readiness.h"
#include "version_cache.h"

#if !defined(_M_IX86)
#error Blind Soldier Version proxy must be built for x86.
#endif

#include <array>
#include <string>

extern "C" FARPROC g_versionExports[17] = {};

namespace {

HMODULE g_proxyModule = nullptr;
HMODULE g_systemVersion = nullptr;
volatile LONG g_forwardingState = 0;

constexpr LONG kForwardingPending = 0;
constexpr LONG kForwardingReady = 1;
constexpr LONG kForwardingFailed = 2;
constexpr ULONGLONG kForwardingReadyTimeoutMilliseconds = 5000ULL;
constexpr std::array<const char*, 17> kVersionExportNames = {
    "GetFileVersionInfoA",
    "GetFileVersionInfoByHandle",
    "GetFileVersionInfoExA",
    "GetFileVersionInfoExW",
    "GetFileVersionInfoSizeA",
    "GetFileVersionInfoSizeExA",
    "GetFileVersionInfoSizeExW",
    "GetFileVersionInfoSizeW",
    "GetFileVersionInfoW",
    "VerFindFileA",
    "VerFindFileW",
    "VerInstallFileA",
    "VerInstallFileW",
    "VerLanguageNameA",
    "VerLanguageNameW",
    "VerQueryValueA",
    "VerQueryValueW"
};

void ShowStartupFailure(const std::wstring& cause) {
    const std::wstring message =
        L"Blind Soldier could not start Final Fantasy VII.\n\nCause: " +
        cause +
        L"\n\nAction: Extract the complete Blind Soldier package again, then restart the game.";
    MessageBoxW(nullptr, message.c_str(), L"Blind Soldier",
                MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
}

bool ResolveVersionExports(
    HMODULE module, std::array<FARPROC, 17>& resolved) {
    if (!module || module == g_proxyModule) return false;
    for (size_t index = 0; index < kVersionExportNames.size(); ++index) {
        resolved[index] = GetProcAddress(module, kVersionExportNames[index]);
        if (!resolved[index]) return false;
    }
    return true;
}

void PublishVersionExports(const std::array<FARPROC, 17>& resolved) {
    for (size_t index = 0; index < resolved.size(); ++index) {
        InterlockedCompareExchangePointer(
            reinterpret_cast<PVOID volatile*>(&g_versionExports[index]),
            reinterpret_cast<PVOID>(resolved[index]), nullptr);
    }
}

bool LoadSystemVersion(std::wstring& diagnostic) {
    std::array<wchar_t, MAX_PATH> directory{};
    const UINT length = GetSystemDirectoryW(
        directory.data(), static_cast<UINT>(directory.size()));
    if (length == 0 || length >= directory.size()) {
        diagnostic = L"The canonical Windows system directory is unavailable.";
        return false;
    }
    std::wstring path(directory.data(), length);
    path += L"\\version.dll";
    std::array<FARPROC, 17> resolved{};
    HMODULE candidate = LoadLibraryW(path.c_str());
    if (ResolveVersionExports(candidate, resolved)) {
        PublishVersionExports(resolved);
        g_systemVersion = candidate;
        return true;
    }

    // FFNx can return the already-loading local version.dll even for the
    // absolute System32 path. Resolve a distinct cached basename outside
    // DllMain so no loader/cache work runs while the loader lock is held.
    blind_soldier::ValidatedVersionCacheLease cached;
    if (!blind_soldier::BuildCachedSystemVersion(path, cached)) {
        const DWORD error = GetLastError();
        diagnostic =
            L"The Windows version library could not be prepared for FFNx. Error " +
            std::to_wstring(error) + L".";
        return false;
    }
    candidate = LoadLibraryExW(cached.path().c_str(), nullptr,
                               LOAD_WITH_ALTERED_SEARCH_PATH);
    resolved.fill(nullptr);
    if (!ResolveVersionExports(candidate, resolved)) {
        const DWORD error = GetLastError();
        diagnostic =
            L"The Windows version library could not be loaded without recursion. Error " +
            std::to_wstring(error) + L".";
        return false;
    }
    PublishVersionExports(resolved);
    g_systemVersion = candidate;
    return true;
}

DWORD WINAPI BootstrapMonitor(void*) {
    blind_soldier::InitializePortableBootstrap(
        g_proxyModule, false, L"Version", true,
        [](const blind_soldier::fs::path& processImage,
           const blind_soldier::HostValidationResult& host,
           blind_soldier::Logger& log) {
            return blind_soldier::WaitForStockRuntimeReadiness(
                processImage, host, log);
        });
    blind_soldier::WaitForPortableBootstrap();
    return 0;
}

FARPROC PublishedVersionExport(DWORD index) {
    return reinterpret_cast<FARPROC>(InterlockedCompareExchangePointer(
        reinterpret_cast<PVOID volatile*>(&g_versionExports[index]),
        nullptr, nullptr));
}

[[noreturn]] void FailVersionForwardingWithoutUi(const wchar_t* diagnostic) {
    OutputDebugStringW(diagnostic);
    InterlockedExchange(&g_forwardingState, kForwardingFailed);
    TerminateProcess(GetCurrentProcess(), 0xB51D0002u);
    ExitProcess(0xB51D0002u);
}

FARPROC WaitForPublishedVersionExport(DWORD index) {
    const ULONGLONG deadline =
        GetTickCount64() + kForwardingReadyTimeoutMilliseconds;
    for (;;) {
        const LONG state =
            InterlockedCompareExchange(&g_forwardingState, 0, 0);
        if (state == kForwardingReady) break;
        if (state == kForwardingFailed) {
            FailVersionForwardingWithoutUi(
                L"Blind Soldier Version forwarding initialization failed.");
        }
        if (GetTickCount64() >= deadline) {
            FailVersionForwardingWithoutUi(
                L"Blind Soldier Version forwarding initialization timed out.");
        }
        Sleep(1);
    }
    FARPROC target = PublishedVersionExport(index);
    if (!target) {
        FailVersionForwardingWithoutUi(
            L"Blind Soldier Version export target was not published.");
    }
    return target;
}

[[noreturn]] void FailVersionInitialization(const std::wstring& diagnostic) {
    InterlockedExchange(&g_forwardingState, kForwardingFailed);
    ShowStartupFailure(diagnostic);
    TerminateProcess(GetCurrentProcess(), 0xB51D0002u);
    ExitProcess(0xB51D0002u);
}

DWORD WINAPI VersionInitializationWorker(void*) {
    std::wstring diagnostic;
    if (!LoadSystemVersion(diagnostic)) {
        FailVersionInitialization(diagnostic);
    }
    InterlockedExchange(&g_forwardingState, kForwardingReady);
    return BootstrapMonitor(nullptr);
}

}  // namespace

extern "C" FARPROC __cdecl ResolveVersionExport(DWORD index) {
    if (index >= kVersionExportNames.size()) {
        FailVersionForwardingWithoutUi(
            L"The Version proxy received an invalid export index.");
    }
    return WaitForPublishedVersionExport(index);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    g_proxyModule = instance;
    HANDLE worker = CreateThread(nullptr, 0, VersionInitializationWorker,
                                 nullptr, 0, nullptr);
    if (!worker) {
        InterlockedExchange(&g_forwardingState, kForwardingFailed);
        return FALSE;
    }
    CloseHandle(worker);
    return TRUE;
}

#define BS_VERSION_FORWARD(stub, index)                 \
extern "C" __declspec(naked) void stub() {             \
    __asm push index                                     \
    __asm call ResolveVersionExport                      \
    __asm add esp, 4                                     \
    __asm jmp eax                                        \
}

#include "version_exports.inc"

#undef BS_VERSION_FORWARD
