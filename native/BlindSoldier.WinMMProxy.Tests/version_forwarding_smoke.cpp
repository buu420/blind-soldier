#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <winver.h>

#include "../BlindSoldier.Common/pe_image.h"
#include "../BlindSoldier.VersionProxy/version_cache.h"

#include <algorithm>
#include <array>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>
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

bool VersionBootstrapLogExists(const fs::path& executableDirectory) {
    const fs::path pattern = executableDirectory / L"Blind-Soldier" / L"Logs" /
        L"Blind-Soldier-Version-*.log";
    WIN32_FIND_DATAW entry{};
    HANDLE search = FindFirstFileW(pattern.c_str(), &entry);
    if (search == INVALID_HANDLE_VALUE) return false;
    FindClose(search);
    return true;
}

std::wstring RemovedManagedReadyEventName() {
    return L"Local\\BlindSoldier.ManagedReady." +
        std::to_wstring(GetCurrentProcessId());
}

struct TempDirectory {
    fs::path path;
    ~TempDirectory() {
        std::error_code ignored;
        fs::remove_all(path, ignored);
    }
};

fs::path CreateTempDirectory() {
    std::array<wchar_t, 32768> temporary{};
    const DWORD length = GetTempPathW(
        static_cast<DWORD>(temporary.size()), temporary.data());
    if (length == 0 || length >= temporary.size()) return {};
    for (DWORD attempt = 0; attempt < 64; ++attempt) {
        const fs::path candidate = fs::path(temporary.data()) /
            (L"blind-soldier-version-cache-tests-" +
             std::to_wstring(GetCurrentProcessId()) + L"-" +
             std::to_wstring(GetTickCount64()) + L"-" +
             std::to_wstring(attempt));
        if (CreateDirectoryW(candidate.c_str(), nullptr)) return candidate;
        if (GetLastError() != ERROR_ALREADY_EXISTS) return {};
    }
    return {};
}

bool WriteBytes(const fs::path& path, const std::vector<uint8_t>& bytes) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    if (!output) return false;
    output.write(reinterpret_cast<const char*>(bytes.data()),
                 static_cast<std::streamsize>(bytes.size()));
    return output.good();
}

bool IsContentAddressedCacheName(const fs::path& path) {
    const std::wstring name = path.filename().wstring();
    constexpr wchar_t prefix[] = L"version-system-x86-";
    constexpr wchar_t suffix[] = L".dll";
    const size_t prefixLength = ARRAYSIZE(prefix) - 1;
    const size_t suffixLength = ARRAYSIZE(suffix) - 1;
    if (name.size() != prefixLength + 64 + suffixLength ||
        name.compare(0, prefixLength, prefix) != 0 ||
        name.compare(name.size() - suffixLength, suffixLength, suffix) != 0) {
        return false;
    }
    for (size_t index = prefixLength; index < prefixLength + 64; ++index) {
        const wchar_t value = name[index];
        if (!((value >= L'0' && value <= L'9') ||
              (value >= L'A' && value <= L'F'))) {
            return false;
        }
    }
    return true;
}

bool TestSameSizeCorruptCandidate(const fs::path& source,
                                  const fs::path& root) {
    const fs::path cache = root / L"same-size-corrupt";
    fs::create_directories(cache);
    blind_soldier::ValidatedVersionCacheLease cached;
    if (!blind_soldier::BuildCachedSystemVersion(source, cache, cached)) {
        std::wcerr << L"Could not seed same-size cache candidate.\n";
        return false;
    }
    const fs::path cachedPath = cached.path();
    const auto original =
        blind_soldier::InspectPeImage(cachedPath).fileBytes;
    if (original.size() < 64) return false;
    auto corrupt = original;
    corrupt[0] ^= 0xFF;
    cached.Reset();
    if (!WriteBytes(cachedPath, corrupt)) return false;
    blind_soldier::ValidatedVersionCacheLease result;
    const bool accepted =
        blind_soldier::BuildCachedSystemVersion(source, cache, result);
    if (accepted) {
        std::wcerr << L"Same-size corrupt cached file was accepted.\n";
        return false;
    }
    return fs::exists(cachedPath) &&
        fs::file_size(cachedPath) == original.size();
}

bool TestWrongMachineCandidate(const fs::path& source, const fs::path& root) {
    const fs::path fixture = root / L"wrong-machine-source.dll";
    auto image = blind_soldier::InspectPeImage(source);
    if (!image.valid || image.fileBytes.size() < 64) return false;
    auto bytes = image.fileBytes;
    const uint32_t peOffset =
        static_cast<uint32_t>(bytes[0x3C]) |
        (static_cast<uint32_t>(bytes[0x3D]) << 8) |
        (static_cast<uint32_t>(bytes[0x3E]) << 16) |
        (static_cast<uint32_t>(bytes[0x3F]) << 24);
    if (peOffset > bytes.size() - 6) return false;
    bytes[peOffset + 4] = 0x64;
    bytes[peOffset + 5] = 0x86;
    if (!WriteBytes(fixture, bytes)) return false;
    const auto wrong = blind_soldier::InspectPeImage(fixture);
    if (!wrong.valid || wrong.machine != IMAGE_FILE_MACHINE_AMD64) return false;

    blind_soldier::ValidatedVersionCacheLease rejectedSource;
    if (blind_soldier::BuildCachedSystemVersion(
            fixture, root / L"wrong-machine-source-cache", rejectedSource)) {
        return false;
    }

    const fs::path cache = root / L"wrong-machine-candidate-cache";
    fs::create_directories(cache);
    blind_soldier::ValidatedVersionCacheLease candidate;
    if (!blind_soldier::BuildCachedSystemVersion(source, cache, candidate)) {
        return false;
    }
    const fs::path candidatePath = candidate.path();
    candidate.Reset();
    if (!WriteBytes(candidatePath, bytes)) return false;
    blind_soldier::ValidatedVersionCacheLease rejectedCandidate;
    return !blind_soldier::BuildCachedSystemVersion(
        source, cache, rejectedCandidate);
}

bool CreateJunction(const fs::path& junction, const fs::path& target) {
    if (!CreateDirectoryW(target.c_str(), nullptr) ||
        GetFullPathNameW(target.c_str(), 0, nullptr, nullptr) == 0) {
        return false;
    }
    std::array<wchar_t, 32768> commandProcessor{};
    const DWORD length = GetEnvironmentVariableW(
        L"COMSPEC", commandProcessor.data(),
        static_cast<DWORD>(commandProcessor.size()));
    if (length == 0 || length >= commandProcessor.size()) return false;
    std::wstring command = L"\"" +
        std::wstring(commandProcessor.data(), length) +
        L"\" /d /q /c mklink /J \"" + junction.wstring() +
        L"\" \"" + fs::absolute(target).wstring() + L"\" >nul";
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(commandProcessor.data(), command.data(), nullptr,
                        nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr,
                        &startup, &process)) {
        return false;
    }
    CloseHandle(process.hThread);
    const DWORD wait = WaitForSingleObject(process.hProcess, 10000);
    DWORD exitCode = MAXDWORD;
    if (wait == WAIT_OBJECT_0) {
        GetExitCodeProcess(process.hProcess, &exitCode);
    } else {
        TerminateProcess(process.hProcess, 1);
    }
    CloseHandle(process.hProcess);
    return wait == WAIT_OBJECT_0 && exitCode == 0;
}

bool TestReparseCollision(const fs::path& source, const fs::path& root) {
    const fs::path cache = root / L"reparse-collision";
    fs::create_directories(cache);
    blind_soldier::ValidatedVersionCacheLease cached;
    if (!blind_soldier::BuildCachedSystemVersion(source, cache, cached)) {
        return false;
    }
    const fs::path cachedPath = cached.path();
    cached.Reset();
    if (!DeleteFileW(cachedPath.c_str()) ||
        !CreateJunction(cachedPath, root / L"junction-target")) {
        std::wcerr << L"Could not create junction collision.\n";
        return false;
    }
    const DWORD attributes = GetFileAttributesW(cachedPath.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES ||
        (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0) {
        return false;
    }
    blind_soldier::ValidatedVersionCacheLease result;
    return !blind_soldier::BuildCachedSystemVersion(source, cache, result);
}

bool TestContentChangeChangesName(const fs::path& source,
                                  const fs::path& root) {
    const fs::path fixture = root / L"changing-source.dll";
    if (!CopyFileW(source.c_str(), fixture.c_str(), TRUE)) return false;
    WIN32_FILE_ATTRIBUTE_DATA originalData{};
    if (!GetFileAttributesExW(fixture.c_str(), GetFileExInfoStandard,
                              &originalData)) {
        return false;
    }
    const fs::path cache = root / L"content-change-cache";
    fs::create_directories(cache);
    blind_soldier::ValidatedVersionCacheLease first;
    if (!blind_soldier::BuildCachedSystemVersion(fixture, cache, first)) {
        return false;
    }
    const fs::path firstPath = first.path();
    auto bytes = blind_soldier::InspectPeImage(fixture).fileBytes;
    if (bytes.empty()) return false;
    bytes.back() ^= 0x5A;
    if (!WriteBytes(fixture, bytes)) return false;
    HANDLE file = CreateFileW(fixture.c_str(), FILE_WRITE_ATTRIBUTES,
                              FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    const BOOL restored = SetFileTime(
        file, &originalData.ftCreationTime, &originalData.ftLastAccessTime,
        &originalData.ftLastWriteTime);
    CloseHandle(file);
    if (!restored || !blind_soldier::InspectPeImage(fixture).valid) return false;
    blind_soldier::ValidatedVersionCacheLease second;
    if (!blind_soldier::BuildCachedSystemVersion(
            fixture, cache, second)) return false;
    const fs::path secondPath = second.path();
    return firstPath != secondPath &&
        IsContentAddressedCacheName(firstPath) &&
        IsContentAddressedCacheName(secondPath);
}

size_t CountTemporaryFiles(const fs::path& directory) {
    size_t count = 0;
    for (const auto& entry : fs::directory_iterator(directory)) {
        if (entry.path().extension() == L".tmp") ++count;
    }
    return count;
}

bool PartialCopyFailure(const fs::path&, const fs::path& destination,
                        void*) {
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE,
                              FILE_SHARE_READ, nullptr, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    const std::array<uint8_t, 32> partial{};
    DWORD written = 0;
    const BOOL writeResult = WriteFile(file, partial.data(),
                                       static_cast<DWORD>(partial.size()),
                                       &written, nullptr);
    CloseHandle(file);
    if (!writeResult || written != partial.size()) return false;
    SetLastError(ERROR_WRITE_FAULT);
    return false;
}

bool CorruptCopy(const fs::path& source, const fs::path& destination,
                 void*) {
    if (!CopyFileW(source.c_str(), destination.c_str(), TRUE)) return false;
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE,
                              FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    const uint8_t corrupt = 0;
    DWORD written = 0;
    const BOOL writeResult = WriteFile(file, &corrupt, 1, &written, nullptr);
    CloseHandle(file);
    return writeResult && written == 1;
}

bool RefusePublish(const fs::path&, const fs::path&, void*) {
    SetLastError(ERROR_ACCESS_DENIED);
    return false;
}

bool RefuseDelete(const fs::path&, void*) {
    SetLastError(ERROR_ACCESS_DENIED);
    return false;
}

bool TestCacheResultPinsIdentity(const fs::path& source,
                                 const fs::path& root) {
    const fs::path cache = root / L"pinned-result";
    fs::create_directories(cache);
    blind_soldier::ValidatedVersionCacheLease cached;
    if (!blind_soldier::BuildCachedSystemVersion(source, cache, cached)) {
        return false;
    }
    HANDLE replacement = CreateFileW(
        cached.path().c_str(), GENERIC_WRITE | DELETE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (replacement != INVALID_HANDLE_VALUE) {
        CloseHandle(replacement);
        std::wcerr <<
            L"Validated cache result did not retain a restrictive handle.\n";
        return false;
    }
    return GetLastError() == ERROR_SHARING_VIOLATION;
}

bool TestOwnedTemporaryCleanup(const fs::path& source,
                               const fs::path& root) {
    struct FailureCase {
        const wchar_t* name;
        blind_soldier::VersionCacheCopyFile copy;
        blind_soldier::VersionCacheMoveFile move;
    };
    const FailureCase cases[] = {
        {L"partial-copy", PartialCopyFailure, nullptr},
        {L"validation", CorruptCopy, nullptr},
        {L"publish", nullptr, RefusePublish},
    };
    for (const auto& failure : cases) {
        const fs::path cache = root / failure.name;
        fs::create_directories(cache);
        blind_soldier::VersionCacheBuildOptions options{};
        options.copyFile = failure.copy;
        options.moveFile = failure.move;
        blind_soldier::ValidatedVersionCacheLease cached;
        if (blind_soldier::BuildCachedSystemVersion(
                source, cache, cached, &options) ||
            CountTemporaryFiles(cache) != 0) {
            std::wcerr << L"Owned temporary survived " << failure.name
                       << L" failure.\n";
            return false;
        }
    }

    const fs::path cache = root / L"cleanup-error";
    fs::create_directories(cache);
    blind_soldier::VersionCacheBuildOptions options{};
    options.copyFile = PartialCopyFailure;
    options.deleteFile = RefuseDelete;
    blind_soldier::ValidatedVersionCacheLease cached;
    const bool succeeded = blind_soldier::BuildCachedSystemVersion(
        source, cache, cached, &options);
    const DWORD error = GetLastError();
    if (succeeded || error != ERROR_ACCESS_DENIED ||
        CountTemporaryFiles(cache) != 1) {
        return false;
    }
    for (const auto& entry : fs::directory_iterator(cache)) {
        if (entry.path().extension() == L".tmp") {
            DeleteFileW(entry.path().c_str());
        }
    }
    return CountTemporaryFiles(cache) == 0;
}

struct RaceBarrier {
    HANDLE ready = nullptr;
    HANDLE release = nullptr;
};

bool WaitImmediatelyBeforePublish(void* context) {
    auto* barrier = static_cast<RaceBarrier*>(context);
    if (!ReleaseSemaphore(barrier->ready, 1, nullptr)) return false;
    const DWORD wait = WaitForSingleObject(barrier->release, 15000);
    if (wait == WAIT_OBJECT_0) return true;
    SetLastError(wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError());
    return false;
}

std::wstring QuoteArgument(const std::wstring& argument) {
    std::wstring quoted = L"\"";
    size_t slashes = 0;
    for (const wchar_t character : argument) {
        if (character == L'\\') {
            ++slashes;
        } else {
            if (character == L'\"') quoted.append(slashes + 1, L'\\');
            quoted.append(slashes, L'\\');
            slashes = 0;
            quoted.push_back(character);
        }
    }
    quoted.append(slashes * 2, L'\\');
    quoted.push_back(L'\"');
    return quoted;
}

bool TestCrossProcessPublication(const fs::path& source,
                                 const fs::path& root) {
    constexpr size_t processCount = 6;
    const fs::path fixture = root / L"race-source.dll";
    auto bytes = blind_soldier::InspectPeImage(source).fileBytes;
    if (bytes.empty()) return false;
    bytes.resize(bytes.size() + 4 * 1024 * 1024, 0xA5);
    if (!WriteBytes(fixture, bytes) ||
        !blind_soldier::InspectPeImage(fixture).valid) {
        return false;
    }
    const fs::path cache = root / L"race-cache";
    fs::create_directories(cache);

    wchar_t executableBuffer[MAX_PATH * 4]{};
    if (!GetModuleFileNameW(nullptr, executableBuffer,
                            ARRAYSIZE(executableBuffer))) return false;
    const fs::path executable = executableBuffer;
    const std::wstring unique = std::to_wstring(GetCurrentProcessId()) + L"." +
        std::to_wstring(GetTickCount64());
    const std::wstring readyName =
        L"Local\\BlindSoldier.VersionCacheRace.Ready." + unique;
    const std::wstring releaseName =
        L"Local\\BlindSoldier.VersionCacheRace.Release." + unique;
    HANDLE ready = CreateSemaphoreW(nullptr, 0, processCount,
                                    readyName.c_str());
    HANDLE release = CreateEventW(nullptr, TRUE, FALSE, releaseName.c_str());
    if (!ready || !release) {
        if (ready) CloseHandle(ready);
        if (release) CloseHandle(release);
        return false;
    }

    std::array<HANDLE, processCount> processes{};
    bool spawned = true;
    for (size_t index = 0; index < processCount; ++index) {
        std::wstring command = QuoteArgument(executable.wstring()) +
            L" --cache-race-child " + QuoteArgument(fixture.wstring()) +
            L" " + QuoteArgument(cache.wstring()) + L" " +
            QuoteArgument(readyName) + L" " + QuoteArgument(releaseName);
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(executable.c_str(), command.data(), nullptr,
                            nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr,
                            &startup, &process)) {
            spawned = false;
            break;
        }
        CloseHandle(process.hThread);
        processes[index] = process.hProcess;
    }
    bool synchronized = spawned;
    if (synchronized) {
        for (size_t index = 0; index < processCount; ++index) {
            if (WaitForSingleObject(ready, 15000) != WAIT_OBJECT_0) {
                synchronized = false;
                break;
            }
        }
    }
    SetEvent(release);

    size_t publishedResults = 0;
    size_t raceWinnerResults = 0;
    for (HANDLE process : processes) {
        if (!process) continue;
        const DWORD wait = WaitForSingleObject(process, 15000);
        DWORD exitCode = MAXDWORD;
        if (wait == WAIT_OBJECT_0) GetExitCodeProcess(process, &exitCode);
        else TerminateProcess(process, 99);
        CloseHandle(process);
        if (exitCode == 60) ++publishedResults;
        if (exitCode == 61) ++raceWinnerResults;
    }
    CloseHandle(release);
    CloseHandle(ready);

    size_t published = 0;
    for (const auto& entry : fs::directory_iterator(cache)) {
        if (IsContentAddressedCacheName(entry.path())) ++published;
    }
    const bool passed = synchronized && publishedResults == 1 &&
        raceWinnerResults == processCount - 1 && published == 1 &&
        CountTemporaryFiles(cache) == 0;
    if (passed) {
        std::cout << "      publication outcomes: Published="
                  << publishedResults << ", RaceWinner(ERROR_ALREADY_EXISTS)="
                  << raceWinnerResults << "\n";
    }
    return passed;
}

bool FindLoadedCachedVersion(const fs::path& cacheDirectory,
                             fs::path& loaded) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,
                                               GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    bool found = false;
    if (Module32FirstW(snapshot, &entry)) {
        do {
            const fs::path candidate = entry.szExePath;
            if (IsCachedSystemVersion(candidate) &&
                EqualPath(candidate.parent_path(), cacheDirectory)) {
                loaded = candidate;
                found = true;
                break;
            }
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return found;
}

int RunCacheIntegrityTests(const fs::path& source) {
    TempDirectory temporary{CreateTempDirectory()};
    if (temporary.path.empty()) return 40;
    struct CacheCase {
        const char* name;
        bool (*run)(const fs::path&, const fs::path&);
    };
    const CacheCase cases[] = {
        {"same-size corrupt candidate", TestSameSizeCorruptCandidate},
        {"wrong-machine source and candidate", TestWrongMachineCandidate},
        {"reparse-point collision", TestReparseCollision},
        {"source-content address change", TestContentChangeChangesName},
        {"validated result pins cache identity", TestCacheResultPinsIdentity},
        {"owned temporary cleanup", TestOwnedTemporaryCleanup},
        {"cross-process identical publication", TestCrossProcessPublication},
    };
    int failures = 0;
    for (const auto& test : cases) {
        if (test.run(source, temporary.path)) {
            std::cout << "  [+] " << test.name << "\n";
        } else {
            std::cerr << "  [-] " << test.name << "\n";
            ++failures;
        }
    }
    if (failures == 0) {
        std::cout << "Version cache integrity passed (7 cases).\n";
    }
    return failures == 0 ? 0 : 41;
}

int RunProxyFallbackIntegration(const fs::path& executable) {
    const fs::path root = executable.parent_path();
    const fs::path localProxy = root /
        (executable.filename().wstring() + L".local") / L"version.dll";
    const fs::path localAppData = root / L"IsolatedLocalAppData";
    if (!CreateDirectoryW(localAppData.c_str(), nullptr) &&
        GetLastError() != ERROR_ALREADY_EXISTS) {
        return 50;
    }
    if (!SetEnvironmentVariableW(L"LOCALAPPDATA", localAppData.c_str())) {
        return 51;
    }
    const HMODULE proxyModule = LoadLibraryW(localProxy.c_str());
    if (!proxyModule) return 52;
    using LanguageNameW = DWORD (WINAPI*)(DWORD, LPWSTR, DWORD);
    const auto languageNameW = reinterpret_cast<LanguageNameW>(
        GetProcAddress(proxyModule, "VerLanguageNameW"));
    wchar_t languageName[128]{};
    if (!languageNameW ||
        languageNameW(GetUserDefaultLangID(), languageName,
                      ARRAYSIZE(languageName)) == 0) return 53;

    const fs::path cacheDirectory =
        localAppData / L"Blind Soldier" / L"NativeCache";
    fs::path loadedCache;
    const ULONGLONG deadline = GetTickCount64() + 5000ULL;
    do {
        if (FindLoadedCachedVersion(cacheDirectory, loadedCache)) break;
        Sleep(10);
    } while (GetTickCount64() < deadline);
    if (loadedCache.empty() || !IsContentAddressedCacheName(loadedCache) ||
        EqualPath(localProxy, loadedCache)) {
        std::wcerr << L"Proxy fallback did not load its isolated cache.\n";
        return 55;
    }
    std::cout << "Proxy-owned Version fallback integration passed.\n";
    return 0;
}

int RunCacheRaceChild(int argc, wchar_t** argv) {
    if (argc != 6) return 62;
    HANDLE ready = OpenSemaphoreW(SEMAPHORE_MODIFY_STATE, FALSE, argv[4]);
    HANDLE release = OpenEventW(SYNCHRONIZE, FALSE, argv[5]);
    if (!ready || !release) {
        if (ready) CloseHandle(ready);
        if (release) CloseHandle(release);
        return 63;
    }
    RaceBarrier barrier{ready, release};
    blind_soldier::VersionCachePublicationResult publication =
        blind_soldier::VersionCachePublicationResult::None;
    blind_soldier::VersionCacheBuildOptions options{};
    options.context = &barrier;
    options.beforePublish = WaitImmediatelyBeforePublish;
    options.publicationResult = &publication;
    blind_soldier::ValidatedVersionCacheLease cached;
    const bool succeeded = blind_soldier::BuildCachedSystemVersion(
        argv[2], argv[3], cached, &options);
    const DWORD error = GetLastError();
    CloseHandle(release);
    CloseHandle(ready);
    if (!succeeded) return 64;
    if (publication ==
            blind_soldier::VersionCachePublicationResult::Published &&
        error == ERROR_SUCCESS) return 60;
    if (publication ==
            blind_soldier::VersionCachePublicationResult::RaceWinner &&
        error == ERROR_ALREADY_EXISTS) return 61;
    return 65;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    wchar_t executable[MAX_PATH * 4]{};
    if (GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable)) == 0) {
        return 10;
    }
    const fs::path proxy = fs::path(executable).parent_path() / L"version.dll";
    if (argc >= 2 && wcscmp(argv[1], L"--cache-race-child") == 0)
        return RunCacheRaceChild(argc, argv);
    if (argc == 2 && wcscmp(argv[1], L"--cache-tests") == 0) {
        wchar_t systemDirectory[MAX_PATH * 4]{};
        if (GetSystemDirectoryW(systemDirectory,
                                ARRAYSIZE(systemDirectory)) == 0) {
            return 42;
        }
        return RunCacheIntegrityTests(
            fs::path(systemDirectory) / L"version.dll");
    }
    if (argc == 2 && wcscmp(argv[1], L"--proxy-fallback") == 0)
        return RunProxyFallbackIntegration(executable);
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
    if (argc == 2 && wcscmp(argv[1], L"--load-only") == 0) {
        wchar_t systemDirectory[MAX_PATH * 4]{};
        if (GetSystemDirectoryW(systemDirectory,
                                ARRAYSIZE(systemDirectory)) == 0) {
            return 32;
        }
        const fs::path systemVersion =
            fs::path(systemDirectory) / L"version.dll";
        const fs::path executableDirectory =
            fs::path(executable).parent_path();
        const ULONGLONG deadline = GetTickCount64() + 5000ULL;
        do {
            if (LoadedProxyAndSystemImplementation(proxy, systemVersion) &&
                VersionBootstrapLogExists(executableDirectory)) {
                std::cout <<
                    "Version proxy load-only bootstrap startup passed.\n";
                return 0;
            }
            Sleep(10);
        } while (GetTickCount64() < deadline);
        std::wcerr <<
            L"Version proxy did not start bootstrap after load-only startup.\n";
        return 33;
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
