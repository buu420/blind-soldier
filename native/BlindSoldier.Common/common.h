#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <cstdio>
#include <cwctype>
#include <stdexcept>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")

namespace blind_soldier {
namespace fs = std::filesystem;

constexpr const wchar_t* SHARED_HOOKS_MOD_ID = L"reloaded.sharedlib.hooks";
constexpr const wchar_t* ACCESSIBILITY_MOD_ID = L"ff7.accessibility.reloaded";
constexpr const wchar_t* READY_EVENT_PREFIX = L"Local\\BlindSoldier.Ready.";

class Logger {
public:
    void Open(const fs::path& dir, const wchar_t* filename) {
        if (m_file) return;
        try {
            std::error_code error;
            fs::create_directories(dir, error);
            if (error) return;
            fs::path path = dir / filename;
            _wfopen_s(&m_file, path.c_str(), L"a+b");
            if (m_file) {
                if (_fseeki64(m_file, 0, SEEK_END) == 0 &&
                    _ftelli64(m_file) == 0) {
                    const unsigned char bom[3] = {0xEF, 0xBB, 0xBF};
                    fwrite(bom, 1, 3, m_file);
                    fflush(m_file);
                }
            }
            m_path = path;
        } catch (...) {
            m_file = nullptr;
        }
    }

    void Close() {
        if (m_file) {
            fclose(m_file);
            m_file = nullptr;
        }
    }

    const fs::path& Path() const { return m_path; }

    void W(const std::wstring& message) {
        if (!m_file) return;
        WritePrefix();
        WriteUtf8(message);
        fwrite("\r\n", 1, 2, m_file);
        fflush(m_file);
    }

    void A(const char* message) {
        if (!m_file || !message) return;
        WritePrefix();
        fwrite(message, 1, strlen(message), m_file);
        fwrite("\r\n", 1, 2, m_file);
        fflush(m_file);
    }

    void Err(const std::wstring& where, DWORD error) {
        W(where + L": err=" + std::to_wstring(error) + L" (" +
          FormatWin32Error(error) + L")");
    }

    static std::wstring FormatWin32Error(DWORD error) {
        if (error == 0) return L"(no error)";
        LPWSTR buffer = nullptr;
        DWORD length = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
                FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, error, 0, reinterpret_cast<LPWSTR>(&buffer), 0, nullptr);
        std::wstring result;
        if (length && buffer) {
            result.assign(buffer, length);
            while (!result.empty() &&
                   (result.back() == L'\n' || result.back() == L'\r' ||
                    result.back() == L' ')) {
                result.pop_back();
            }
            LocalFree(buffer);
        }
        if (result.empty()) result = L"(no message)";
        return result;
    }

private:
    FILE* m_file = nullptr;
    fs::path m_path;

    void WritePrefix() {
        SYSTEMTIME time;
        GetLocalTime(&time);
        char prefix[64];
        int length = _snprintf_s(
            prefix, sizeof(prefix), _TRUNCATE,
            "[%04u-%02u-%02u %02u:%02u:%02u.%03u] ", time.wYear,
            time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond,
            time.wMilliseconds);
        if (length > 0) fwrite(prefix, 1, static_cast<size_t>(length), m_file);
    }

    void WriteUtf8(const std::wstring& text) {
        if (text.empty()) return;
        int length = WideCharToMultiByte(CP_UTF8, 0, text.c_str(),
                                         static_cast<int>(text.size()), nullptr,
                                         0, nullptr, nullptr);
        if (length <= 0) return;
        std::vector<char> buffer(static_cast<size_t>(length));
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(),
                            static_cast<int>(text.size()), buffer.data(),
                            length, nullptr, nullptr);
        fwrite(buffer.data(), 1, static_cast<size_t>(length), m_file);
    }
};

inline std::wstring ToLower(std::wstring value) {
    for (auto& character : value) character = static_cast<wchar_t>(towlower(character));
    return value;
}

inline std::wstring Utf8ToWide(const std::string& value) {
    if (value.empty()) return {};
    int length = MultiByteToWideChar(CP_UTF8, 0, value.c_str(),
                                     static_cast<int>(value.size()), nullptr, 0);
    std::wstring result(static_cast<size_t>(length), 0);
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(),
                        static_cast<int>(value.size()), result.data(), length);
    return result;
}

inline std::wstring JsonEscape(const std::wstring& value) {
    std::wstring result;
    result.reserve(value.size());
    for (wchar_t character : value) {
        switch (character) {
            case L'\\': result += L"\\\\"; break;
            case L'"': result += L"\\\""; break;
            case L'\n': result += L"\\n"; break;
            case L'\r': result += L"\\r"; break;
            case L'\t': result += L"\\t"; break;
            default: result += character; break;
        }
    }
    return result;
}

inline fs::path SelfPath() {
    wchar_t buffer[MAX_PATH * 4];
    DWORD length = GetModuleFileNameW(nullptr, buffer, ARRAYSIZE(buffer));
    return fs::path(buffer, buffer + length);
}

inline fs::path SelfDir() { return SelfPath().parent_path(); }

inline fs::path AppDataRoaming() {
    PWSTR path = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &path)))
        return {};
    fs::path result(path);
    CoTaskMemFree(path);
    return result;
}

inline fs::path ReloadedIIPointerFile() {
    auto appData = AppDataRoaming();
    if (appData.empty()) return {};
    return appData / L"Reloaded-Mod-Loader-II" / L"ReloadedII.json";
}

inline bool WriteUtf8FileAtomic(const fs::path& path,
                                const std::wstring& content) {
    std::error_code error;
    fs::create_directories(path.parent_path(), error);
    if (error) return false;

    int length = WideCharToMultiByte(CP_UTF8, 0, content.c_str(),
                                     static_cast<int>(content.size()), nullptr,
                                     0, nullptr, nullptr);
    if (length <= 0 && !content.empty()) return false;
    std::vector<char> buffer(static_cast<size_t>(length));
    if (length > 0) {
        WideCharToMultiByte(CP_UTF8, 0, content.c_str(),
                            static_cast<int>(content.size()), buffer.data(),
                            length, nullptr, nullptr);
    }

    fs::path temporary = path;
    temporary += L".blind_soldier." + std::to_wstring(GetCurrentProcessId()) +
                 L"." + std::to_wstring(GetCurrentThreadId()) + L".tmp";
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr,
                              CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
                              nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;

    bool success = true;
    if (length > 0) {
        DWORD written = 0;
        success = WriteFile(file, buffer.data(), static_cast<DWORD>(length),
                            &written, nullptr) &&
                  written == static_cast<DWORD>(length);
    }
    if (success) success = FlushFileBuffers(file) != FALSE;
    CloseHandle(file);
    if (success) {
        success = MoveFileExW(temporary.c_str(), path.c_str(),
                              MOVEFILE_REPLACE_EXISTING |
                                  MOVEFILE_WRITE_THROUGH) != FALSE;
    }
    if (!success) DeleteFileW(temporary.c_str());
    return success;
}

inline bool ReadUtf8File(const fs::path& path, std::string& content) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return false;
    content.assign(std::istreambuf_iterator<char>(input),
                   std::istreambuf_iterator<char>());
    return input.good() || input.eof();
}

inline std::wstring ReloadedIIPointerContent(const fs::path& installRoot) {
    return
        L"{\n"
        L"  \"LoaderPath32\": \"" + JsonEscape((installRoot / L"Loader" / L"X86" / L"Reloaded.Mod.Loader.dll").wstring()) + L"\",\n"
        L"  \"LoaderPath64\": \"" + JsonEscape((installRoot / L"Loader" / L"X64" / L"Reloaded.Mod.Loader.dll").wstring()) + L"\",\n"
        L"  \"LauncherPath\": \"\",\n"
        L"  \"Bootstrapper32Path\": \"" + JsonEscape((installRoot / L"Loader" / L"X86" / L"Bootstrapper" / L"Reloaded.Mod.Loader.Bootstrapper.dll").wstring()) + L"\",\n"
        L"  \"Bootstrapper64Path\": \"" + JsonEscape((installRoot / L"Loader" / L"X64" / L"Bootstrapper" / L"Reloaded.Mod.Loader.Bootstrapper.dll").wstring()) + L"\",\n"
        L"  \"ApplicationConfigDirectory\": \"" + JsonEscape((installRoot / L"Apps").wstring()) + L"\",\n"
        L"  \"ModUserConfigDirectory\": \"" + JsonEscape((installRoot / L"User" / L"Mods").wstring()) + L"\",\n"
        L"  \"MiscConfigDirectory\": \"" + JsonEscape((installRoot / L"User" / L"Misc").wstring()) + L"\",\n"
        L"  \"PluginConfigDirectory\": \"" + JsonEscape((installRoot / L"Plugins").wstring()) + L"\",\n"
        L"  \"ModConfigDirectory\": \"" + JsonEscape((installRoot / L"Mods").wstring()) + L"\",\n"
        L"  \"EnabledPlugins\": [],\n"
        L"  \"FirstLaunch\": false,\n"
        L"  \"ShowConsole\": false\n"
        L"}\n";
}

inline bool WriteReloadedIIPointerAt(const fs::path& pointer,
                                     const fs::path& installRoot,
                                     Logger& log) {
    if (pointer.empty()) {
        log.A("WriteReloadedIIPointer: AppData path empty");
        return false;
    }
    bool written = WriteUtf8FileAtomic(
        pointer, ReloadedIIPointerContent(installRoot));
    log.W(std::wstring(L"WriteReloadedIIPointer: ") +
          (written ? L"OK" : L"FAILED") + L" path=" + pointer.wstring());
    return written;
}

inline bool WriteReloadedIIPointer(const fs::path& installRoot, Logger& log) {
    return WriteReloadedIIPointerAt(ReloadedIIPointerFile(), installRoot, log);
}

inline bool WriteAppConfig(const fs::path& reloadedRoot,
                           const std::wstring& executableName,
                           const fs::path& executablePath,
                           Logger& log) {
    std::wstring appId = ToLower(executableName);
    fs::path appDirectory = reloadedRoot / L"Apps" / appId;
    std::wstring content =
        L"{\n"
        L"  \"AppId\": \"" + JsonEscape(appId) + L"\",\n"
        L"  \"AppName\": \"Final Fantasy VII with Blind Soldier\",\n"
        L"  \"AppLocation\": \"" + JsonEscape(executablePath.wstring()) + L"\",\n"
        L"  \"AppArguments\": \"\",\n"
        L"  \"AppIcon\": \"\",\n"
        L"  \"AutoInject\": false,\n"
        L"  \"EnabledMods\": [\n"
        L"    \"" + std::wstring(SHARED_HOOKS_MOD_ID) + L"\",\n"
        L"    \"" + std::wstring(ACCESSIBILITY_MOD_ID) + L"\"\n"
        L"  ],\n"
        L"  \"WorkingDirectory\": \"" + JsonEscape(executablePath.parent_path().wstring()) + L"\",\n"
        L"  \"PluginData\": {},\n"
        L"  \"SortedMods\": [\n"
        L"    \"" + std::wstring(SHARED_HOOKS_MOD_ID) + L"\",\n"
        L"    \"" + std::wstring(ACCESSIBILITY_MOD_ID) + L"\"\n"
        L"  ],\n"
        L"  \"PreserveDisabledModOrder\": true,\n"
        L"  \"DontInject\": false,\n"
        L"  \"IsMsStore\": false\n"
        L"}\n";
    fs::path path = appDirectory / L"AppConfig.json";
    bool written = WriteUtf8FileAtomic(path, content);
    log.W(std::wstring(L"WriteAppConfig(") + executableName + L"): " +
          (written ? L"OK" : L"FAILED") + L" path=" + path.wstring());
    return written;
}

}  // namespace blind_soldier
