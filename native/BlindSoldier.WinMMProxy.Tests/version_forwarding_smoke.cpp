#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <winver.h>

#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace {

bool EqualPath(const fs::path& left, const fs::path& right) {
    std::error_code leftError;
    std::error_code rightError;
    const fs::path canonicalLeft = fs::weakly_canonical(left, leftError);
    const fs::path canonicalRight = fs::weakly_canonical(right, rightError);
    return !leftError && !rightError &&
        _wcsicmp(canonicalLeft.c_str(), canonicalRight.c_str()) == 0;
}

bool IsCachedSystemVersion(const fs::path& path) {
    std::wstring name = path.filename().wstring();
    for (wchar_t& character : name) {
        character = static_cast<wchar_t>(towlower(character));
    }
    return name.rfind(L"version-system-x86-", 0) == 0 &&
        path.extension() == L".dll";
}

bool LoadedProxyAndSystemImplementation(const fs::path& proxy,
                                        const fs::path& systemVersion) {
    bool proxyFound = false;
    bool implementationFound = false;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,
                                               GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    if (Module32FirstW(snapshot, &entry)) {
        do {
            proxyFound = proxyFound || EqualPath(entry.szExePath, proxy);
            if (!EqualPath(entry.szExePath, proxy)) {
                implementationFound = implementationFound ||
                    EqualPath(entry.szExePath, systemVersion) ||
                    IsCachedSystemVersion(entry.szExePath);
            }
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return proxyFound && implementationFound;
}

std::wstring RemovedManagedReadyEventName() {
    return L"Local\\BlindSoldier.ManagedReady." +
        std::to_wstring(GetCurrentProcessId());
}

}  // namespace

int wmain() {
    wchar_t executable[MAX_PATH * 4]{};
    if (GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable)) == 0) {
        return 10;
    }
    const fs::path proxy = fs::path(executable).parent_path() / L"version.dll";
    const std::wstring managedReadyEventName = RemovedManagedReadyEventName();
    SetLastError(ERROR_SUCCESS);
    HANDLE unexpectedEvent = OpenEventW(
        SYNCHRONIZE, FALSE, managedReadyEventName.c_str());
    if (unexpectedEvent) {
        CloseHandle(unexpectedEvent);
        std::wcerr << L"Managed-ready event existed before Version proxy load.\n";
        return 24;
    }
    if (GetLastError() != ERROR_FILE_NOT_FOUND) {
        std::wcerr << L"Unexpected pre-load managed-ready OpenEvent error: "
                   << GetLastError() << L"\n";
        return 25;
    }
    const HMODULE proxyModule = LoadLibraryW(proxy.c_str());
    if (!proxyModule) {
        std::wcerr << L"Could not load the local Version proxy: "
                   << GetLastError() << L"\n";
        return 22;
    }
    {
        wchar_t immediateSystemDirectory[MAX_PATH * 4]{};
        if (GetSystemDirectoryW(immediateSystemDirectory,
                                ARRAYSIZE(immediateSystemDirectory)) == 0) {
            return 30;
        }
        const fs::path immediateSystemVersion =
            fs::path(immediateSystemDirectory) / L"version.dll";
        using ImmediateGetSizeW = DWORD (WINAPI*)(LPCWSTR, LPDWORD);
        using ImmediateLanguageNameW = DWORD (WINAPI*)(DWORD, LPWSTR, DWORD);
        const auto immediateGetSizeW = reinterpret_cast<ImmediateGetSizeW>(
            GetProcAddress(proxyModule, "GetFileVersionInfoSizeW"));
        const auto immediateLanguageNameW =
            reinterpret_cast<ImmediateLanguageNameW>(
                GetProcAddress(proxyModule, "VerLanguageNameW"));
        DWORD ignored = 0;
        wchar_t languageName[128]{};
        if (!immediateGetSizeW || !immediateLanguageNameW ||
            immediateGetSizeW(immediateSystemVersion.c_str(), &ignored) == 0 ||
            immediateLanguageNameW(GetUserDefaultLangID(), languageName,
                                   ARRAYSIZE(languageName)) == 0) {
            std::wcerr << L"Immediate Version forwarding failed after load: "
                       << GetLastError() << L"\n";
            return 31;
        }
    }
    SetLastError(ERROR_SUCCESS);
    HANDLE removedEvent = OpenEventW(
        SYNCHRONIZE, FALSE, managedReadyEventName.c_str());
    if (removedEvent) {
        CloseHandle(removedEvent);
        std::wcerr << L"Version proxy recreated the removed managed-ready event.\n";
        return 26;
    }
    if (GetLastError() != ERROR_FILE_NOT_FOUND) {
        std::wcerr << L"Unexpected post-load managed-ready OpenEvent error: "
                   << GetLastError() << L"\n";
        return 27;
    }
    constexpr const char* requiredExports[] = {
        "GetFileVersionInfoA", "GetFileVersionInfoByHandle",
        "GetFileVersionInfoExA", "GetFileVersionInfoExW",
        "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA",
        "GetFileVersionInfoSizeExW", "GetFileVersionInfoSizeW",
        "GetFileVersionInfoW", "VerFindFileA", "VerFindFileW",
        "VerInstallFileA", "VerInstallFileW", "VerLanguageNameA",
        "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW"
    };
    for (const char* name : requiredExports) {
        if (!GetProcAddress(proxyModule, name)) {
            std::cerr << "Missing Version export: " << name << "\n";
            return 28;
        }
    }
    using GetSizeW = DWORD (WINAPI*)(LPCWSTR, LPDWORD);
    using GetInfoW = BOOL (WINAPI*)(LPCWSTR, DWORD, DWORD, LPVOID);
    using QueryW = BOOL (WINAPI*)(LPCVOID, LPCWSTR, LPVOID*, PUINT);
    using GetSizeA = DWORD (WINAPI*)(LPCSTR, LPDWORD);
    using GetInfoA = BOOL (WINAPI*)(LPCSTR, DWORD, DWORD, LPVOID);
    using QueryA = BOOL (WINAPI*)(LPCVOID, LPCSTR, LPVOID*, PUINT);
    const auto getSizeW = reinterpret_cast<GetSizeW>(
        GetProcAddress(proxyModule, "GetFileVersionInfoSizeW"));
    const auto getInfoW = reinterpret_cast<GetInfoW>(
        GetProcAddress(proxyModule, "GetFileVersionInfoW"));
    const auto queryW = reinterpret_cast<QueryW>(
        GetProcAddress(proxyModule, "VerQueryValueW"));
    const auto getSizeA = reinterpret_cast<GetSizeA>(
        GetProcAddress(proxyModule, "GetFileVersionInfoSizeA"));
    const auto getInfoA = reinterpret_cast<GetInfoA>(
        GetProcAddress(proxyModule, "GetFileVersionInfoA"));
    const auto queryA = reinterpret_cast<QueryA>(
        GetProcAddress(proxyModule, "VerQueryValueA"));
    if (!getSizeW || !getInfoW || !queryW ||
        !getSizeA || !getInfoA || !queryA) {
        return 23;
    }
    wchar_t systemDirectory[MAX_PATH * 4]{};
    if (GetSystemDirectoryW(systemDirectory, ARRAYSIZE(systemDirectory)) == 0) {
        return 12;
    }
    const fs::path systemVersion = fs::path(systemDirectory) / L"version.dll";
    DWORD ignored = 0;
    const DWORD wideSize = getSizeW(systemVersion.c_str(), &ignored);
    if (wideSize == 0) {
        std::wcerr << L"Forwarded GetFileVersionInfoSizeW failed: "
                   << GetLastError() << L"\n";
        return 11;
    }
    std::vector<BYTE> wideVersion(wideSize);
    if (!getInfoW(systemVersion.c_str(), 0, wideSize, wideVersion.data())) {
        return 14;
    }
    void* wideRoot = nullptr;
    UINT wideRootSize = 0;
    if (!queryW(wideVersion.data(), L"\\", &wideRoot, &wideRootSize) ||
        !wideRoot || wideRootSize == 0) {
        return 15;
    }

    wchar_t systemVersionBuffer[MAX_PATH * 4]{};
    if (GetSystemDirectoryW(systemVersionBuffer,
                            ARRAYSIZE(systemVersionBuffer)) == 0) {
        return 16;
    }
    const std::wstring systemVersionWide =
        std::wstring(systemVersionBuffer) + L"\\version.dll";
    const int narrowLength = WideCharToMultiByte(
        CP_ACP, 0, systemVersionWide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (narrowLength <= 0) return 17;
    std::vector<char> systemVersionNarrow(static_cast<size_t>(narrowLength));
    if (WideCharToMultiByte(CP_ACP, 0, systemVersionWide.c_str(), -1,
                            systemVersionNarrow.data(), narrowLength,
                            nullptr, nullptr) == 0) {
        return 18;
    }
    const DWORD narrowSize = getSizeA(systemVersionNarrow.data(), &ignored);
    if (narrowSize == 0) return 19;
    std::vector<BYTE> narrowVersion(narrowSize);
    if (!getInfoA(systemVersionNarrow.data(), 0, narrowSize,
                  narrowVersion.data())) {
        return 20;
    }
    void* narrowRoot = nullptr;
    UINT narrowRootSize = 0;
    if (!queryA(narrowVersion.data(), "\\", &narrowRoot, &narrowRootSize) ||
                        !narrowRoot ||
                        narrowRootSize == 0) {
        return 21;
    }
    Sleep(750);
    if (!LoadedProxyAndSystemImplementation(proxy, systemVersion)) {
        std::wcerr << L"Local proxy and a distinct system implementation were not both loaded.\n";
        return 13;
    }
    std::cout << "Version proxy forwarding passed.\n";
    return 0;
}
