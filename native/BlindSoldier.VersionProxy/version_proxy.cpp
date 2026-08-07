#include "../BlindSoldier.WinMMProxy/proxy_state.h"
#include "app_loader_readiness.h"

#if !defined(_M_IX86)
#error Blind Soldier Version proxy must be built for x86.
#endif

#include <array>
#include <string>
#include <vector>

extern "C" FARPROC g_versionExports[17] = {};

namespace {

HMODULE g_proxyModule = nullptr;
HMODULE g_systemVersion = nullptr;
volatile LONG g_bootstrapMonitorStarted = 0;

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

bool EnsureDirectory(const std::wstring& path) {
    if (CreateDirectoryW(path.c_str(), nullptr)) return true;
    if (GetLastError() != ERROR_ALREADY_EXISTS) return false;
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
        (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool BuildCachedSystemVersion(const std::wstring& source,
                              std::wstring& cached) {
    WIN32_FILE_ATTRIBUTE_DATA sourceData{};
    if (!GetFileAttributesExW(source.c_str(), GetFileExInfoStandard,
                              &sourceData)) {
        return false;
    }
    std::array<wchar_t, 32768> localAppData{};
    const DWORD localLength = GetEnvironmentVariableW(
        L"LOCALAPPDATA", localAppData.data(),
        static_cast<DWORD>(localAppData.size()));
    if (localLength == 0 || localLength >= localAppData.size()) return false;

    const std::wstring productDirectory =
        std::wstring(localAppData.data(), localLength) + L"\\Blind Soldier";
    const std::wstring cacheDirectory = productDirectory + L"\\NativeCache";
    if (!EnsureDirectory(productDirectory) ||
        !EnsureDirectory(cacheDirectory)) {
        return false;
    }

    ULARGE_INTEGER size{};
    size.LowPart = sourceData.nFileSizeLow;
    size.HighPart = sourceData.nFileSizeHigh;
    ULARGE_INTEGER modified{};
    modified.LowPart = sourceData.ftLastWriteTime.dwLowDateTime;
    modified.HighPart = sourceData.ftLastWriteTime.dwHighDateTime;
    cached = cacheDirectory + L"\\version-system-x86-" +
        std::to_wstring(size.QuadPart) + L"-" +
        std::to_wstring(modified.QuadPart) + L".dll";

    if (!CopyFileW(source.c_str(), cached.c_str(), TRUE)) {
        const DWORD error = GetLastError();
        if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS) {
            return false;
        }
    }
    WIN32_FILE_ATTRIBUTE_DATA cachedData{};
    if (!GetFileAttributesExW(cached.c_str(), GetFileExInfoStandard,
                              &cachedData)) {
        return false;
    }
    return cachedData.nFileSizeLow == sourceData.nFileSizeLow &&
        cachedData.nFileSizeHigh == sourceData.nFileSizeHigh;
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
    std::wstring cached;
    if (!BuildCachedSystemVersion(path, cached)) {
        const DWORD error = GetLastError();
        diagnostic =
            L"The Windows version library could not be prepared for FFNx. Error " +
            std::to_wstring(error) + L".";
        return false;
    }
    candidate = LoadLibraryExW(cached.c_str(), nullptr,
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

bool StartBootstrapMonitor() {
    HANDLE thread = CreateThread(nullptr, 0, BootstrapMonitor,
                                 nullptr, 0, nullptr);
    if (!thread) return false;
    CloseHandle(thread);
    return true;
}

FARPROC PublishedVersionExport(DWORD index) {
    return reinterpret_cast<FARPROC>(InterlockedCompareExchangePointer(
        reinterpret_cast<PVOID volatile*>(&g_versionExports[index]),
        nullptr, nullptr));
}

[[noreturn]] void FailVersionInitialization(const std::wstring& diagnostic) {
    ShowStartupFailure(diagnostic);
    TerminateProcess(GetCurrentProcess(), 0xB51D0002u);
    ExitProcess(0xB51D0002u);
}

}  // namespace

extern "C" FARPROC __cdecl ResolveVersionExport(DWORD index) {
    if (index >= kVersionExportNames.size()) {
        FailVersionInitialization(
            L"The Version proxy received an invalid export index.");
    }

    FARPROC target = PublishedVersionExport(index);
    if (!target) {
        std::wstring diagnostic;
        if (!LoadSystemVersion(diagnostic)) {
            target = PublishedVersionExport(index);
            if (!target) FailVersionInitialization(diagnostic);
        } else {
            target = PublishedVersionExport(index);
        }
    }
    if (!target) {
        FailVersionInitialization(
            L"The Windows version export target is unavailable.");
    }

    if (InterlockedCompareExchange(&g_bootstrapMonitorStarted, 1, 0) == 0 &&
        !StartBootstrapMonitor()) {
        FailVersionInitialization(
            L"The x86 accessibility bootstrap thread could not start.");
    }
    return target;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) g_proxyModule = instance;
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
