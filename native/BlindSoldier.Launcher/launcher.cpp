// Blind Soldier native FFVII launcher
//
// This retains the launcher supplied in stuff.zip: IFEO invokes this process,
// it recreates the real game suspended with a debugger-only recursion bypass,
// injects Reloaded's matching bootstrapper, resumes the game, and waits for it.

#include "../BlindSoldier.Common/common.h"
#include <tlhelp32.h>

using namespace blind_soldier;

constexpr const wchar_t* DIALOG_TITLE = L"Blind Soldier Accessibility Mod";
constexpr const wchar_t* RECURSION_ENV_VAR = L"BLIND_SOLDIER_LAUNCHER_ACTIVE";

#ifdef _WIN64
constexpr const wchar_t* LOG_NAME = L"Blind-Soldier-Launcher-x64.log";
constexpr const wchar_t* LOADER_ARCHITECTURE = L"X64";
constexpr const wchar_t* MOD_ARCHITECTURE = L"x64";
constexpr const wchar_t* ACCESSIBILITY_ASSEMBLY =
    L"Ff7.Accessibility.Steam2026X64.dll";
#else
constexpr const wchar_t* LOG_NAME = L"Blind-Soldier-Launcher-x86.log";
constexpr const wchar_t* LOADER_ARCHITECTURE = L"X86";
constexpr const wchar_t* MOD_ARCHITECTURE = L"x86";
constexpr const wchar_t* ACCESSIBILITY_ASSEMBLY =
    L"Ff7.Accessibility.Reloaded.dll";
#endif

static void ShowError(const std::wstring& message) {
    MessageBoxW(nullptr, message.c_str(), DIALOG_TITLE, MB_OK | MB_ICONERROR);
}

struct AppDataSwap {
    fs::path pointer;
    fs::path backup;
    std::string portableContent;
    bool hadOriginal = false;
    bool ready = false;
    HANDLE mutex = nullptr;
    bool ownsMutex = false;
    Logger* log = nullptr;

    AppDataSwap(const fs::path& installRoot, Logger& logger,
                const fs::path& pointerOverride = {}) : log(&logger) {
        pointer = pointerOverride.empty() ? ReloadedIIPointerFile()
                                          : pointerOverride;
        if (pointer.empty()) {
            log->A("AppDataSwap: AppData path empty");
            return;
        }

        // ReloadedII.json is global to the current Windows user. Hold the
        // mutex for the entire game lifetime so two FFVII instances cannot
        // overwrite or restore the pointer out of order.
        unsigned long long hash = 1469598103934665603ULL;
        for (wchar_t character : ToLower(pointer.wstring())) {
            hash ^= static_cast<unsigned long long>(character);
            hash *= 1099511628211ULL;
        }
        std::wstring mutexName =
            L"Global\\BlindSoldier.ReloadedPointer." + std::to_wstring(hash);
        mutex = CreateMutexW(nullptr, FALSE, mutexName.c_str());
        if (!mutex) {
            log->Err(L"AppDataSwap: CreateMutexW", GetLastError());
            return;
        }
        DWORD mutexWait = WaitForSingleObject(mutex, INFINITE);
        if (mutexWait != WAIT_OBJECT_0 && mutexWait != WAIT_ABANDONED) {
            log->Err(L"AppDataSwap: WaitForSingleObject mutex", GetLastError());
            return;
        }
        ownsMutex = true;
        if (mutexWait == WAIT_ABANDONED)
            log->A("AppDataSwap: recovering after abandoned pointer mutex");

        std::wstring content = ReloadedIIPointerContent(installRoot);
        int utf8Length = WideCharToMultiByte(
            CP_UTF8, 0, content.c_str(), static_cast<int>(content.size()),
            nullptr, 0, nullptr, nullptr);
        if (utf8Length <= 0) return;
        portableContent.resize(static_cast<size_t>(utf8Length));
        WideCharToMultiByte(CP_UTF8, 0, content.c_str(),
                            static_cast<int>(content.size()),
                            portableContent.data(), utf8Length, nullptr,
                            nullptr);

        std::error_code error;
        fs::create_directories(pointer.parent_path(), error);
        if (error) {
            log->W(L"AppDataSwap: create_directories failed: " +
                   Utf8ToWide(error.message()));
            return;
        }
        backup = pointer;
        backup += L".blind_soldier_backup";

        // A durable backup can remain only when a prior launcher died before
        // its destructor ran. Recover it before beginning a new swap.
        if (fs::exists(backup, error)) {
            if (fs::exists(pointer, error)) {
                std::string current;
                if (!ReadUtf8File(pointer, current) ||
                    current != portableContent) {
                    log->A("AppDataSwap: durable backup and an external pointer "
                           "both exist; refusing to overwrite either");
                    return;
                }
            }
            if (!MoveFileExW(backup.c_str(), pointer.c_str(),
                             MOVEFILE_REPLACE_EXISTING |
                                 MOVEFILE_WRITE_THROUGH)) {
                log->Err(L"AppDataSwap: recover durable backup", GetLastError());
                return;
            }
            log->A("AppDataSwap: recovered durable backup from prior launch");
        } else if (fs::exists(pointer, error)) {
            std::string current;
            if (ReadUtf8File(pointer, current) && current == portableContent) {
                if (!DeleteFileW(pointer.c_str())) {
                    log->Err(L"AppDataSwap: remove abandoned portable pointer",
                             GetLastError());
                    return;
                }
                log->A("AppDataSwap: removed abandoned portable pointer");
            }
        }

        if (fs::exists(pointer, error)) {
            if (!MoveFileExW(pointer.c_str(), backup.c_str(),
                             MOVEFILE_REPLACE_EXISTING |
                                 MOVEFILE_WRITE_THROUGH)) {
                log->Err(L"AppDataSwap: create durable backup", GetLastError());
                return;
            }
            hadOriginal = true;
        }

        if (!WriteReloadedIIPointerAt(pointer, installRoot, *log)) {
            RestoreAfterFailedWrite();
            return;
        }
        ready = true;
    }

    ~AppDataSwap() {
        if (ready && log) {
            std::string current;
            bool stillOurs = ReadUtf8File(pointer, current) &&
                             current == portableContent;
            if (!stillOurs) {
                log->A("AppDataSwap: pointer changed externally; leaving it and "
                       "the durable backup untouched");
            } else if (hadOriginal) {
                if (MoveFileExW(backup.c_str(), pointer.c_str(),
                                MOVEFILE_REPLACE_EXISTING |
                                    MOVEFILE_WRITE_THROUGH)) {
                    log->A("AppDataSwap: restored original Reloaded pointer");
                } else {
                    log->Err(L"AppDataSwap: restore original pointer",
                             GetLastError());
                }
            } else if (DeleteFileW(pointer.c_str()) ||
                       GetLastError() == ERROR_FILE_NOT_FOUND) {
                log->A("AppDataSwap: removed portable Reloaded pointer");
            } else {
                log->Err(L"AppDataSwap: remove portable pointer",
                         GetLastError());
            }
        }
        if (ownsMutex) ReleaseMutex(mutex);
        if (mutex) CloseHandle(mutex);
    }

    bool Ready() const { return ready; }

private:
    void RestoreAfterFailedWrite() {
        if (!hadOriginal) return;
        if (MoveFileExW(backup.c_str(), pointer.c_str(),
                        MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            log->A("AppDataSwap: restored original after write failure");
            hadOriginal = false;
        } else {
            log->Err(L"AppDataSwap: restore after write failure",
                     GetLastError());
        }
    }
};

enum class InjectResult {
    Success,
    AllocFailed,
    WriteFailed,
    ResolveFailed,
    CreateThreadFailed,
    TimedOut,
    LoadLibraryFailed
};

static const wchar_t* InjectResultName(InjectResult result) {
    switch (result) {
        case InjectResult::Success: return L"Success";
        case InjectResult::AllocFailed: return L"VirtualAllocEx failed";
        case InjectResult::WriteFailed: return L"WriteProcessMemory failed";
        case InjectResult::ResolveFailed:
            return L"GetProcAddress(LoadLibraryW) failed";
        case InjectResult::CreateThreadFailed:
            return L"CreateRemoteThread failed";
        case InjectResult::TimedOut: return L"LoadLibraryW timed out";
        case InjectResult::LoadLibraryFailed:
            return L"LoadLibraryW returned NULL";
        default: return L"unknown";
    }
}

static LPVOID FindRemoteModuleBase(DWORD processId,
                                   const std::wstring& moduleName,
                                   Logger& log) {
    HANDLE snapshot = INVALID_HANDLE_VALUE;
    for (int attempt = 0; attempt < 20; ++attempt) {
        snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
        if (snapshot != INVALID_HANDLE_VALUE) break;
        if (GetLastError() != ERROR_BAD_LENGTH) break;
        Sleep(10);
    }
    if (snapshot == INVALID_HANDLE_VALUE) {
        log.Err(L"FindRemoteModuleBase: CreateToolhelp32Snapshot",
                GetLastError());
        return nullptr;
    }

    MODULEENTRY32W entry = {};
    entry.dwSize = sizeof(entry);
    LPVOID remoteModuleBase = nullptr;
    if (Module32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szModule, moduleName.c_str()) == 0) {
                remoteModuleBase = entry.modBaseAddr;
                break;
            }
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    if (!remoteModuleBase)
        log.W(L"FindRemoteModuleBase: module not found: " + moduleName);
    return remoteModuleBase;
}

static LPTHREAD_START_ROUTINE ResolveRemoteLoadLibraryW(DWORD processId,
                                                        Logger& log) {
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC loadLibrary = kernel32 ? GetProcAddress(kernel32, "LoadLibraryW")
                                   : nullptr;
    if (!loadLibrary) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetProcAddress", GetLastError());
        return nullptr;
    }

    HMODULE localOwner = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(loadLibrary), &localOwner)) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleHandleExW",
                GetLastError());
        return nullptr;
    }
    wchar_t localModulePath[MAX_PATH * 4] = {};
    if (!GetModuleFileNameW(localOwner, localModulePath,
                            ARRAYSIZE(localModulePath))) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleFileNameW",
                GetLastError());
        return nullptr;
    }
    std::wstring ownerName = fs::path(localModulePath).filename().wstring();
    LPVOID remoteModuleBase =
        FindRemoteModuleBase(processId, ownerName, log);
    if (!remoteModuleBase) return nullptr;

    uintptr_t relativeAddress =
        reinterpret_cast<uintptr_t>(loadLibrary) -
        reinterpret_cast<uintptr_t>(localOwner);
    uintptr_t remoteAddress =
        reinterpret_cast<uintptr_t>(remoteModuleBase) + relativeAddress;
    return reinterpret_cast<LPTHREAD_START_ROUTINE>(remoteAddress);
}

static InjectResult InjectDll(HANDLE process, DWORD processId,
                              const std::wstring& dllPath,
                              DWORD timeoutMilliseconds, Logger& log) {
    size_t bytes = (dllPath.size() + 1) * sizeof(wchar_t);
    LPVOID remote = VirtualAllocEx(process, nullptr, bytes,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        log.Err(L"InjectDll: VirtualAllocEx", GetLastError());
        return InjectResult::AllocFailed;
    }
    if (!WriteProcessMemory(process, remote, dllPath.c_str(), bytes, nullptr)) {
        log.Err(L"InjectDll: WriteProcessMemory", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::WriteFailed;
    }
    LPTHREAD_START_ROUTINE remoteLoadLibrary =
        ResolveRemoteLoadLibraryW(processId, log);
    if (!remoteLoadLibrary) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::ResolveFailed;
    }
    HANDLE thread = CreateRemoteThread(
        process, nullptr, 0, remoteLoadLibrary, remote, 0, nullptr);
    if (!thread) {
        log.Err(L"InjectDll: CreateRemoteThread", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::CreateThreadFailed;
    }

    DWORD elapsed = 0;
    constexpr DWORD interval = 5000;
    for (;;) {
        DWORD wait = WaitForSingleObject(thread, interval);
        if (wait == WAIT_OBJECT_0) break;
        if (wait == WAIT_TIMEOUT) {
            elapsed += interval;
            log.W(L"InjectDll: still waiting, milliseconds=" +
                  std::to_wstring(elapsed));
            if (elapsed >= timeoutMilliseconds) {
                CloseHandle(thread);
                VirtualFreeEx(process, remote, 0, MEM_RELEASE);
                return InjectResult::TimedOut;
            }
            continue;
        }
        log.Err(L"InjectDll: WaitForSingleObject", GetLastError());
        CloseHandle(thread);
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::CreateThreadFailed;
    }

    DWORD module = 0;
    GetExitCodeThread(thread, &module);
    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    if (module == 0) return InjectResult::LoadLibraryFailed;
    log.W(L"InjectDll: success, module=" + std::to_wstring(module));
    return InjectResult::Success;
}

static int LaunchGameUnmodded(const fs::path& gameExecutable,
                              const std::wstring& extraArguments,
                              Logger& log) {
    STARTUPINFOW startup = {sizeof(startup)};
    PROCESS_INFORMATION process = {};
    std::wstring command = L"\"" + gameExecutable.wstring() + L"\"";
    if (!extraArguments.empty()) command += L" " + extraArguments;
    std::vector<wchar_t> commandBuffer(command.begin(), command.end());
    commandBuffer.push_back(0);
    fs::path gameDirectory = gameExecutable.parent_path();
    if (!CreateProcessW(gameExecutable.c_str(), commandBuffer.data(), nullptr,
                        nullptr, FALSE, DEBUG_ONLY_THIS_PROCESS, nullptr,
                        gameDirectory.c_str(), &startup, &process)) {
        log.Err(L"LaunchGameUnmodded: CreateProcessW", GetLastError());
        return 1;
    }
    DebugSetProcessKillOnExit(FALSE);
    if (!DebugActiveProcessStop(process.dwProcessId))
        log.Err(L"LaunchGameUnmodded: DebugActiveProcessStop", GetLastError());
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}

static int LaunchWithMod(const fs::path& gameExecutable,
                         const std::wstring& extraArguments,
                         const fs::path& launcherDirectory,
                         Logger& log) {
    fs::path reloaded = launcherDirectory / L"Reloaded-II";
    fs::path loader = reloaded / L"Loader" / LOADER_ARCHITECTURE;
    fs::path bootstrapper = loader / L"Bootstrapper" /
                            L"Reloaded.Mod.Loader.Bootstrapper.dll";
    fs::path loaderDll = loader / L"Reloaded.Mod.Loader.dll";
    fs::path mod = reloaded / L"Mods" / ACCESSIBILITY_MOD_ID;
    fs::path modConfig = mod / L"ModConfig.json";
    fs::path modAssembly = mod / MOD_ARCHITECTURE / ACCESSIBILITY_ASSEMBLY;
    fs::path hooks = reloaded / L"Mods" / SHARED_HOOKS_MOD_ID;
    fs::path hooksConfig = hooks / L"ModConfig.json";
    fs::path hooksAssembly = hooks / MOD_ARCHITECTURE /
                             L"Reloaded.Hooks.ReloadedII.dll";

    std::error_code error;
    std::wstring missing;
    for (const fs::path& path : {bootstrapper, loaderDll, modConfig, modAssembly,
                                 hooksConfig, hooksAssembly}) {
        if (!fs::exists(path, error)) missing += L"  - " + path.wstring() + L"\n";
    }
    if (!missing.empty()) {
        log.W(L"LaunchWithMod: required files missing:\n" + missing);
        ShowError(L"Blind Soldier files are missing. The game will launch "
                  L"without the accessibility mod.\n\nMissing:\n" + missing +
                  L"\nRe-extract the complete Blind Soldier ZIP to fix "
                  L"this.\n\nLog: " + log.Path().wstring());
        return LaunchGameUnmodded(gameExecutable, extraArguments, log);
    }

    if (!WriteAppConfig(reloaded, gameExecutable.filename().wstring(),
                        gameExecutable, log)) {
        ShowError(L"Blind Soldier could not write its game configuration. "
                  L"Final Fantasy VII was not started because accessibility "
                  L"could not be guaranteed.\n\nCheck folder permissions, then "
                  L"try again.\n\nLog: " + log.Path().wstring());
        return 1;
    }
    AppDataSwap swap(reloaded, log);
    if (!swap.Ready()) {
        ShowError(L"Blind Soldier could not safely activate its Reloaded "
                  L"configuration. Final Fantasy VII was not started because "
                  L"accessibility could not be guaranteed.\n\nClose any other "
                  L"Final Fantasy VII instance and try again.\n\nLog: " +
                  log.Path().wstring());
        return 1;
    }

    STARTUPINFOW startup = {sizeof(startup)};
    PROCESS_INFORMATION process = {};
    std::wstring command = L"\"" + gameExecutable.wstring() + L"\"";
    if (!extraArguments.empty()) command += L" " + extraArguments;
    std::vector<wchar_t> commandBuffer(command.begin(), command.end());
    commandBuffer.push_back(0);
    fs::path gameDirectory = gameExecutable.parent_path();
    log.W(L"LaunchWithMod: CreateProcessW suspended: " + command);
    if (!CreateProcessW(gameExecutable.c_str(), commandBuffer.data(), nullptr,
                        nullptr, FALSE,
                        DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED, nullptr,
                        gameDirectory.c_str(), &startup, &process)) {
        DWORD win32Error = GetLastError();
        log.Err(L"LaunchWithMod: CreateProcessW", win32Error);
        ShowError(L"Could not launch Final Fantasy VII.\n\nWin32 error " +
                  std::to_wstring(win32Error) + L" (" +
                  Logger::FormatWin32Error(win32Error) + L")\n\nLog: " +
                  log.Path().wstring());
        return 1;
    }

    DebugSetProcessKillOnExit(FALSE);
    if (!DebugActiveProcessStop(process.dwProcessId))
        log.Err(L"LaunchWithMod: DebugActiveProcessStop", GetLastError());

    InjectResult injection = InjectDll(process.hProcess, process.dwProcessId,
                                       bootstrapper.wstring(), 30000, log);
    if (injection != InjectResult::Success) {
        TerminateProcess(process.hProcess, 1);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        ShowError(std::wstring(L"Could not load Blind Soldier: ") +
                  InjectResultName(injection) +
                  L"\n\nThe game was stopped before execution. See the log:\n" +
                  log.Path().wstring());
        return 1;
    }

    DWORD previousSuspendCount = ResumeThread(process.hThread);
    if (previousSuspendCount == static_cast<DWORD>(-1)) {
        log.Err(L"LaunchWithMod: ResumeThread", GetLastError());
        TerminateProcess(process.hProcess, 1);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 1;
    }
    log.W(L"LaunchWithMod: resumed, prior suspend count=" +
          std::to_wstring(previousSuspendCount));

    DWORD heartbeat = 0;
    for (;;) {
        DWORD wait = WaitForSingleObject(process.hProcess, 30000);
        if (wait == WAIT_OBJECT_0) {
            DWORD exitCode = 0;
            GetExitCodeProcess(process.hProcess, &exitCode);
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return static_cast<int>(exitCode);
        }
        if (wait == WAIT_TIMEOUT) {
            log.W(L"LaunchWithMod: game still running, heartbeat=" +
                  std::to_wstring(++heartbeat));
            continue;
        }
        log.Err(L"LaunchWithMod: WaitForSingleObject", GetLastError());
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 1;
    }
}

#ifndef BLIND_SOLDIER_NATIVE_TESTS
int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    int returnCode = 1;
    Logger log;
    try {
        fs::path launcherDirectory = SelfDir();
        log.Open(launcherDirectory, LOG_NAME);
        log.A("=== Blind Soldier Launcher start ===");
        log.W(L"selfPath=" + SelfPath().wstring());
        log.W(L"raw CommandLine=" + std::wstring(GetCommandLineW()));

        int argumentCount = 0;
        LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
        if (!arguments) {
            log.Err(L"CommandLineToArgvW", GetLastError());
            throw std::runtime_error("argument parsing failed");
        }
        if (argumentCount < 2) {
            LocalFree(arguments);
            ShowError(L"This launcher must be invoked by Windows after "
                      L"Blind-Soldier-Installer.exe registers it.\n\nRun "
                      L"Blind-Soldier-Installer.exe to install, or run it with "
                      L"/uninstall to disable the redirect.\n\nLog: " +
                      log.Path().wstring());
            throw std::runtime_error("game path argument missing");
        }

        fs::path gameExecutable = arguments[1];
        std::wstring extraArguments;
        for (int index = 2; index < argumentCount; ++index) {
            if (!extraArguments.empty()) extraArguments += L" ";
            extraArguments += L"\"";
            extraArguments += arguments[index];
            extraArguments += L"\"";
        }
        LocalFree(arguments);

        std::error_code error;
        if (!fs::exists(gameExecutable, error)) {
            ShowError(L"Final Fantasy VII executable not found:\n\n" +
                      gameExecutable.wstring() +
                      L"\n\nRun Blind-Soldier-Installer.exe with /uninstall, "
                      L"verify the game in Steam, then reinstall Blind Soldier."
                      L"\n\nLog: " + log.Path().wstring());
            throw std::runtime_error("game executable missing");
        }

        wchar_t recursionValue[8];
        DWORD recursionLength = GetEnvironmentVariableW(
            RECURSION_ENV_VAR, recursionValue, ARRAYSIZE(recursionValue));
        if (recursionLength > 0 && recursionLength < ARRAYSIZE(recursionValue)) {
            log.A("Recursion guard active; launching game without injection");
            returnCode = LaunchGameUnmodded(gameExecutable, extraArguments, log);
        } else {
            SetEnvironmentVariableW(RECURSION_ENV_VAR, L"1");
            returnCode = LaunchWithMod(gameExecutable, extraArguments,
                                       launcherDirectory, log);
        }
    } catch (const std::exception& exception) {
        log.W(L"wWinMain: std::exception: " + Utf8ToWide(exception.what()));
    } catch (...) {
        log.A("wWinMain: unknown exception");
    }
    log.W(L"wWinMain: rc=" + std::to_wstring(returnCode));
    log.Close();
    CoUninitialize();
    return returnCode;
}
#endif
