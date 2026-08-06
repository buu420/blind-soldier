#include <windows.h>
#include <tlhelp32.h>

#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace {

std::wstring Canonical(const fs::path& path) {
    std::error_code error;
    const fs::path result = fs::weakly_canonical(path, error);
    return error ? std::wstring() : result.native();
}

bool EqualPath(const fs::path& left, const fs::path& right) {
    const std::wstring a = Canonical(left);
    const std::wstring b = Canonical(right);
    return !a.empty() && !b.empty() && _wcsicmp(a.c_str(), b.c_str()) == 0;
}

bool ModulesContainBoth(const fs::path& proxy, const fs::path& system) {
    bool proxyFound = false;
    bool systemFound = false;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,
                                               GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    if (Module32FirstW(snapshot, &entry)) {
        do {
            proxyFound = proxyFound || EqualPath(entry.szExePath, proxy);
            systemFound = systemFound || EqualPath(entry.szExePath, system);
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return proxyFound && systemFound && !EqualPath(proxy, system);
}

bool ProxyLoggedCanonicalSystem(const fs::path& proxy) {
    const fs::path logs = proxy.parent_path() / L"Blind-Soldier" / L"Logs";
    std::error_code error;
    if (!fs::is_directory(logs, error) || error) return false;
    for (const auto& item : fs::directory_iterator(logs, error)) {
        if (error || !item.is_regular_file()) continue;
        std::ifstream input(item.path(), std::ios::binary);
        const std::string content((std::istreambuf_iterator<char>(input)),
                                  std::istreambuf_iterator<char>());
        if (content.find("canonical system WinMM loaded:") !=
                std::string::npos &&
            content.find("winmm.dll") != std::string::npos) {
            return true;
        }
    }
    return false;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc != 2 && argc != 3) {
        std::wcerr << L"Usage: smoke <proxy> <system-winmm>\n";
        return 10;
    }
    wchar_t wow64Directory[32768]{};
    const UINT wow64Length = GetSystemWow64DirectoryW(
        wow64Directory, ARRAYSIZE(wow64Directory));
    std::wcout << L"GetSystemWow64DirectoryW length=" << wow64Length
               << L" error=" << GetLastError() << L" path="
               << wow64Directory << L" first="
               << static_cast<unsigned int>(wow64Directory[0]) << L","
               << static_cast<unsigned int>(wow64Directory[1]) << L"\n";
    if (argc == 2 && wcscmp(argv[1], L"--wow64-only") == 0) return 0;
    if (argc != 3) return 10;
    const fs::path proxyPath = argv[1];
    const fs::path systemPath = argv[2];
    HMODULE system = LoadLibraryW(systemPath.c_str());
    HMODULE proxy = LoadLibraryW(proxyPath.c_str());
    if (!system || !proxy) {
        std::wcerr << L"Could not load both WinMM modules.\n";
        return 11;
    }

    using TimeGetTime = DWORD (WINAPI*)();
    using GetNumDevs = UINT (WINAPI*)();
    using GetErrorString = BOOL (WINAPI*)(DWORD, LPWSTR, UINT);
    const auto systemTime = reinterpret_cast<TimeGetTime>(
        GetProcAddress(system, "timeGetTime"));
    const auto proxyTime = reinterpret_cast<TimeGetTime>(
        GetProcAddress(proxy, "timeGetTime"));
    const auto systemWave = reinterpret_cast<GetNumDevs>(
        GetProcAddress(system, "waveOutGetNumDevs"));
    const auto proxyWave = reinterpret_cast<GetNumDevs>(
        GetProcAddress(proxy, "waveOutGetNumDevs"));
    const auto systemMidi = reinterpret_cast<GetNumDevs>(
        GetProcAddress(system, "midiOutGetNumDevs"));
    const auto proxyMidi = reinterpret_cast<GetNumDevs>(
        GetProcAddress(proxy, "midiOutGetNumDevs"));
    const auto systemError = reinterpret_cast<GetErrorString>(
        GetProcAddress(system, "mciGetErrorStringW"));
    const auto proxyError = reinterpret_cast<GetErrorString>(
        GetProcAddress(proxy, "mciGetErrorStringW"));
    if (!systemTime || !proxyTime || !systemWave || !proxyWave ||
        !systemMidi || !proxyMidi || !systemError || !proxyError) {
        return 12;
    }

    const DWORD before = systemTime();
    const DWORD forwarded = proxyTime();
    const DWORD after = systemTime();
    if (forwarded < before || forwarded > after + 1000 ||
        proxyWave() != systemWave() || proxyMidi() != systemMidi()) {
        return 13;
    }
    wchar_t systemText[256]{};
    wchar_t proxyText[256]{};
    const BOOL systemTextResult = systemError(257, systemText,
                                              ARRAYSIZE(systemText));
    const BOOL proxyTextResult = proxyError(257, proxyText,
                                            ARRAYSIZE(proxyText));
    if (systemTextResult != proxyTextResult ||
        wcscmp(systemText, proxyText) != 0) {
        return 14;
    }
    const bool modulesContainBoth = ModulesContainBoth(proxyPath, systemPath);
    FreeLibrary(proxy);
    const bool loggedCanonicalSystem = ProxyLoggedCanonicalSystem(proxyPath);
    FreeLibrary(system);
    if (!modulesContainBoth) return 15;
    if (!loggedCanonicalSystem) return 16;

    std::cout << "WinMM forwarding smoke passed.\n";
    return 0;
}
