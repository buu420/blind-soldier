// Blind Soldier Accessibility Installer / Uninstaller
//
// This intentionally preserves the installation model supplied in stuff.zip:
// files are extracted into the game folder first, and this executable only
// validates them and writes/removes Image File Execution Options redirects.

#include "../BlindSoldier.Common/common.h"
#include <array>

using namespace blind_soldier;

constexpr const wchar_t* DIALOG_TITLE = L"Blind Soldier Accessibility Mod Installer";
constexpr const wchar_t* LOG_NAME = L"Blind-Soldier-Installer.log";
constexpr const wchar_t* INSTALLER_NAME = L"Blind-Soldier-Installer.exe";
constexpr const wchar_t* LAUNCHER_X86_NAME = L"Blind-Soldier-Launcher-x86.exe";
constexpr const wchar_t* LAUNCHER_X64_NAME = L"Blind-Soldier-Launcher-x64.exe";
constexpr const wchar_t* GAME_X86_EXE_NAME = L"ff7_en.exe";
constexpr const wchar_t* GAME_X64_EXE_NAME = L"FFVII.exe";
constexpr const wchar_t* IFEO_ROOT =
    L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options";
constexpr const wchar_t* IFEO_OWNER_VALUE = L"BlindSoldierDebuggerOwner";
constexpr unsigned REQUIRED_DOTNET_MAJOR = 9;
constexpr unsigned REQUIRED_DOTNET_MINOR = 0;
constexpr unsigned REQUIRED_DOTNET_PATCH = 8;
static HKEY gIfeoHive = HKEY_LOCAL_MACHINE;
static std::wstring gIfeoRoot = IFEO_ROOT;

struct TargetDefinition {
    const wchar_t* executableName;
    const wchar_t* launcherName;
    const wchar_t* loaderArchitecture;
    const wchar_t* modArchitecture;
    const wchar_t* modAssembly;
    const wchar_t* runtimeArchitecture;
    std::array<const wchar_t*, 2> candidatePaths;
};

const TargetDefinition X86_TARGET = {
    GAME_X86_EXE_NAME,
    LAUNCHER_X86_NAME,
    L"X86",
    L"x86",
    L"Ff7.Accessibility.Reloaded.dll",
    L"x86",
    {L"ff7_en.exe", L"ff7\\workingdir\\ff7_en.exe"}
};

const TargetDefinition X64_TARGET = {
    GAME_X64_EXE_NAME,
    LAUNCHER_X64_NAME,
    L"X64",
    L"x64",
    L"Ff7.Accessibility.Steam2026X64.dll",
    L"x64",
    {L"FFVII.exe", L"FFVII.exe"}
};

struct TargetState {
    const TargetDefinition* definition = nullptr;
    bool gamePresent = false;
    fs::path gamePath;
    bool launcherPresent = false;
    bool bootstrapperPresent = false;
    bool loaderPresent = false;
    bool modConfigPresent = false;
    bool modAssemblyPresent = false;
    bool hooksConfigPresent = false;
    bool hooksAssemblyPresent = false;
    bool runtimePresent = false;

    bool RequiredFilesPresent() const {
        return launcherPresent && bootstrapperPresent && loaderPresent &&
               modConfigPresent && modAssemblyPresent && hooksConfigPresent &&
               hooksAssemblyPresent;
    }
};

static void ShowError(const std::wstring& message) {
    MessageBoxW(nullptr, message.c_str(), DIALOG_TITLE, MB_OK | MB_ICONERROR);
}

static void ShowInfo(const std::wstring& message) {
    MessageBoxW(nullptr, message.c_str(), DIALOG_TITLE,
                MB_OK | MB_ICONINFORMATION);
}

static bool AskYesNo(const std::wstring& message) {
    return MessageBoxW(nullptr, message.c_str(), DIALOG_TITLE,
                       MB_YESNO | MB_ICONQUESTION) == IDYES;
}

static bool IsElevated() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    TOKEN_ELEVATION elevation = {};
    DWORD size = sizeof(elevation);
    BOOL success = GetTokenInformation(token, TokenElevation, &elevation, size,
                                       &size);
    CloseHandle(token);
    return success && elevation.TokenIsElevated != 0;
}

static bool RelaunchElevated(const std::wstring& arguments, Logger& log) {
    wchar_t self[MAX_PATH * 4];
    if (!GetModuleFileNameW(nullptr, self, ARRAYSIZE(self))) {
        log.Err(L"RelaunchElevated: GetModuleFileNameW", GetLastError());
        return false;
    }
    log.W(L"RelaunchElevated: runas " + std::wstring(self) + L" args=" +
          arguments);

    SHELLEXECUTEINFOW execute = {};
    execute.cbSize = sizeof(execute);
    execute.fMask = SEE_MASK_NOCLOSEPROCESS;
    execute.lpVerb = L"runas";
    execute.lpFile = self;
    execute.lpParameters = arguments.empty() ? nullptr : arguments.c_str();
    execute.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&execute)) {
        DWORD error = GetLastError();
        log.Err(L"RelaunchElevated: ShellExecuteExW", error);
        if (error == ERROR_CANCELLED) {
            ShowError(L"Administrator access was declined.\n\n"
                      L"Blind Soldier needs administrator rights to register "
                      L"its automatic game launcher. No changes were made.\n\n"
                      L"Run Blind-Soldier-Installer.exe again and accept the "
                      L"Windows security prompt.");
        } else {
            ShowError(L"Could not relaunch as administrator.\n\nWin32 error " +
                      std::to_wstring(error) + L" (" +
                      Logger::FormatWin32Error(error) + L")\n\n"
                      L"Try right-clicking Blind-Soldier-Installer.exe and "
                      L"choosing Run as administrator.");
        }
        return false;
    }
    if (execute.hProcess) CloseHandle(execute.hProcess);
    return true;
}

static bool IsCompatibleRuntimeVersion(const std::wstring& value) {
    unsigned major = 0;
    unsigned minor = 0;
    unsigned patch = 0;
    wchar_t trailing = 0;
    int parsed = swscanf_s(value.c_str(), L"%u.%u.%u%c", &major, &minor,
                           &patch, &trailing, 1);
    if (parsed != 3 || major != REQUIRED_DOTNET_MAJOR) return false;
    return minor > REQUIRED_DOTNET_MINOR ||
           (minor == REQUIRED_DOTNET_MINOR &&
            patch >= REQUIRED_DOTNET_PATCH);
}

static bool HasCompatibleFrameworkDirectory(const fs::path& directory) {
    std::error_code error;
    if (!fs::is_directory(directory, error)) return false;
    for (const auto& item : fs::directory_iterator(directory, error)) {
        if (error) return false;
        if (item.is_directory(error) &&
            IsCompatibleRuntimeVersion(item.path().filename().wstring())) {
            return true;
        }
    }
    return false;
}

static fs::path GetKnownFolder(REFKNOWNFOLDERID identifier) {
    PWSTR value = nullptr;
    if (FAILED(SHGetKnownFolderPath(identifier, 0, nullptr, &value))) return {};
    fs::path result(value);
    CoTaskMemFree(value);
    return result;
}

static fs::path GetEnvironmentPath(const wchar_t* name) {
    DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
    if (!required) return {};
    std::vector<wchar_t> buffer(required);
    if (!GetEnvironmentVariableW(name, buffer.data(), required)) return {};
    return fs::path(buffer.data());
}

static bool HasCompatibleDesktopRuntimeAtRoot(const fs::path& root) {
    if (root.empty()) return false;
    fs::path shared = root / L"shared";
    return HasCompatibleFrameworkDirectory(
               shared / L"Microsoft.WindowsDesktop.App") &&
           HasCompatibleFrameworkDirectory(shared / L"Microsoft.NETCore.App");
}

static bool HasCompatibleDesktopRuntime(const wchar_t* architecture,
                                        Logger& log) {
    std::vector<fs::path> roots;
    if (_wcsicmp(architecture, L"x86") == 0) {
        roots.push_back(GetEnvironmentPath(L"DOTNET_ROOT_X86"));
        fs::path programFiles = GetKnownFolder(FOLDERID_ProgramFilesX86);
        if (!programFiles.empty()) roots.push_back(programFiles / L"dotnet");
    } else {
        roots.push_back(GetEnvironmentPath(L"DOTNET_ROOT_X64"));
        roots.push_back(GetEnvironmentPath(L"DOTNET_ROOT"));
        fs::path programFiles = GetKnownFolder(FOLDERID_ProgramFiles);
        if (!programFiles.empty()) roots.push_back(programFiles / L"dotnet");
    }

    for (const fs::path& root : roots) {
        if (root.empty()) continue;
        fs::path shared = root / L"shared";
        bool desktop = HasCompatibleFrameworkDirectory(
            shared / L"Microsoft.WindowsDesktop.App");
        bool core = HasCompatibleFrameworkDirectory(
            shared / L"Microsoft.NETCore.App");
        log.W(L"HasCompatibleDesktopRuntime(" + std::wstring(architecture) +
              L"): root=" + root.wstring() + L" desktop=" +
              (desktop ? L"yes" : L"no") + L" core=" +
              (core ? L"yes" : L"no"));
        if (HasCompatibleDesktopRuntimeAtRoot(root)) return true;
    }
    return false;
}

static LONG ReadRegistryString(HKEY key, const wchar_t* name,
                               std::wstring& value, bool& exists) {
    exists = false;
    value.clear();
    DWORD type = 0;
    DWORD bytes = 0;
    LONG result = RegQueryValueExW(key, name, nullptr, &type, nullptr, &bytes);
    if (result == ERROR_FILE_NOT_FOUND) return ERROR_SUCCESS;
    if (result != ERROR_SUCCESS) return result;
    if (type != REG_SZ || bytes < sizeof(wchar_t)) return ERROR_INVALID_DATA;
    std::vector<wchar_t> buffer(bytes / sizeof(wchar_t) + 1, 0);
    result = RegQueryValueExW(key, name, nullptr, &type,
                              reinterpret_cast<BYTE*>(buffer.data()), &bytes);
    if (result != ERROR_SUCCESS) return result;
    value.assign(buffer.data());
    exists = true;
    return ERROR_SUCCESS;
}

static LONG WriteRegistryString(HKEY key, const wchar_t* name,
                                const std::wstring& value) {
    DWORD bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
    return RegSetValueExW(key, name, 0, REG_SZ,
                          reinterpret_cast<const BYTE*>(value.c_str()), bytes);
}

static LONG SetIFEODebugger(const std::wstring& targetExecutableName,
                            const std::wstring& debuggerFullPath,
                            bool& created,
                            Logger& log) {
    created = false;
    std::wstring keyPath = gIfeoRoot + L"\\" +
                           targetExecutableName;
    log.W(L"SetIFEODebugger: creating HKLM\\" + keyPath);
    HKEY key = nullptr;
    LONG result = RegCreateKeyExW(
        gIfeoHive, keyPath.c_str(), 0, nullptr,
        REG_OPTION_NON_VOLATILE,
        KEY_QUERY_VALUE | KEY_SET_VALUE | KEY_WOW64_64KEY, nullptr,
        &key, nullptr);
    if (result != ERROR_SUCCESS) {
        log.Err(L"RegCreateKeyExW", static_cast<DWORD>(result));
        return result;
    }
    const std::wstring expectedDebugger = L"\"" + debuggerFullPath + L"\"";
    std::wstring currentDebugger;
    std::wstring currentOwner;
    bool debuggerExists = false;
    bool ownerExists = false;
    result = ReadRegistryString(key, L"Debugger", currentDebugger,
                                debuggerExists);
    if (result == ERROR_SUCCESS)
        result = ReadRegistryString(key, IFEO_OWNER_VALUE, currentOwner,
                                    ownerExists);
    if (result != ERROR_SUCCESS) {
        log.Err(L"SetIFEODebugger: read existing values",
                static_cast<DWORD>(result));
        RegCloseKey(key);
        return result;
    }

    if (debuggerExists &&
        (currentDebugger != expectedDebugger || !ownerExists ||
         currentOwner != expectedDebugger)) {
        log.W(L"SetIFEODebugger: refusing existing unowned Debugger value: " +
              currentDebugger);
        RegCloseKey(key);
        return ERROR_ALREADY_ASSIGNED;
    }
    if (debuggerExists) {
        log.W(L"SetIFEODebugger: exact owned registration already present");
        RegCloseKey(key);
        return ERROR_SUCCESS;
    }
    if (!debuggerExists && ownerExists) {
        result = RegDeleteValueW(key, IFEO_OWNER_VALUE);
        if (result != ERROR_SUCCESS && result != ERROR_FILE_NOT_FOUND) {
            RegCloseKey(key);
            return result;
        }
        ownerExists = false;
    }

    result = WriteRegistryString(key, IFEO_OWNER_VALUE, expectedDebugger);
    if (result == ERROR_SUCCESS)
        result = WriteRegistryString(key, L"Debugger", expectedDebugger);
    if (result != ERROR_SUCCESS) {
        if (ownerExists)
            WriteRegistryString(key, IFEO_OWNER_VALUE, currentOwner);
        else
            RegDeleteValueW(key, IFEO_OWNER_VALUE);
        log.Err(L"SetIFEODebugger: write owned values",
                static_cast<DWORD>(result));
    } else {
        created = true;
        log.W(L"SetIFEODebugger: Debugger = " + expectedDebugger);
    }
    RegCloseKey(key);
    return result;
}

static LONG RemoveIFEODebugger(const std::wstring& targetExecutableName,
                               const std::wstring& debuggerFullPath,
                               Logger& log) {
    std::wstring keyPath = gIfeoRoot + L"\\" +
                           targetExecutableName;
    log.W(L"RemoveIFEODebugger: opening HKLM\\" + keyPath);
    HKEY key = nullptr;
    LONG result = RegOpenKeyExW(
        gIfeoHive, keyPath.c_str(), 0,
        KEY_QUERY_VALUE | KEY_SET_VALUE | KEY_WOW64_64KEY, &key);
    if (result == ERROR_FILE_NOT_FOUND) return ERROR_SUCCESS;
    if (result != ERROR_SUCCESS) {
        log.Err(L"RegOpenKeyExW", static_cast<DWORD>(result));
        return result;
    }
    const std::wstring expectedDebugger = L"\"" + debuggerFullPath + L"\"";
    std::wstring currentDebugger;
    std::wstring currentOwner;
    bool debuggerExists = false;
    bool ownerExists = false;
    result = ReadRegistryString(key, L"Debugger", currentDebugger,
                                debuggerExists);
    if (result == ERROR_SUCCESS)
        result = ReadRegistryString(key, IFEO_OWNER_VALUE, currentOwner,
                                    ownerExists);
    if (result != ERROR_SUCCESS) {
        RegCloseKey(key);
        return result;
    }
    if (!debuggerExists && !ownerExists) {
        RegCloseKey(key);
        return ERROR_SUCCESS;
    }
    if (!debuggerExists || !ownerExists ||
        currentDebugger != expectedDebugger ||
        currentOwner != expectedDebugger) {
        log.W(L"RemoveIFEODebugger: refusing to remove an entry not owned by "
              L"this installation");
        RegCloseKey(key);
        return ERROR_ALREADY_ASSIGNED;
    }

    result = RegDeleteValueW(key, L"Debugger");
    if (result == ERROR_SUCCESS)
        result = RegDeleteValueW(key, IFEO_OWNER_VALUE);
    if (result != ERROR_SUCCESS)
        log.Err(L"RemoveIFEODebugger: delete owned values",
                static_cast<DWORD>(result));
    RegCloseKey(key);
    RegDeleteKeyExW(gIfeoHive, keyPath.c_str(), KEY_WOW64_64KEY, 0);
    return result;
}

static TargetState CheckTarget(const fs::path& directory,
                               const TargetDefinition& definition,
                               Logger& log) {
    TargetState state;
    state.definition = &definition;
    std::error_code error;
    for (const wchar_t* candidate : definition.candidatePaths) {
        fs::path path = directory / candidate;
        if (fs::exists(path, error)) {
            state.gamePresent = true;
            state.gamePath = path;
            break;
        }
    }

    fs::path reloaded = directory / L"Reloaded-II";
    fs::path loader = reloaded / L"Loader" / definition.loaderArchitecture;
    fs::path mod = reloaded / L"Mods" / ACCESSIBILITY_MOD_ID;
    fs::path hooks = reloaded / L"Mods" / SHARED_HOOKS_MOD_ID;
    state.launcherPresent = fs::exists(directory / definition.launcherName, error);
    state.bootstrapperPresent = fs::exists(
        loader / L"Bootstrapper" /
            L"Reloaded.Mod.Loader.Bootstrapper.dll",
        error);
    state.loaderPresent = fs::exists(loader / L"Reloaded.Mod.Loader.dll", error);
    state.modConfigPresent = fs::exists(mod / L"ModConfig.json", error);
    state.modAssemblyPresent = fs::exists(
        mod / definition.modArchitecture / definition.modAssembly, error);
    state.hooksConfigPresent = fs::exists(hooks / L"ModConfig.json", error);
    state.hooksAssemblyPresent = fs::exists(
        hooks / definition.modArchitecture / L"Reloaded.Hooks.ReloadedII.dll",
        error);
    state.runtimePresent = !state.gamePresent ||
        HasCompatibleDesktopRuntime(definition.runtimeArchitecture, log);

    log.W(L"CheckTarget: " + std::wstring(definition.executableName) +
          L" game=" + (state.gamePresent ? state.gamePath.wstring() : L"MISSING") +
          L" launcher=" + (state.launcherPresent ? L"OK" : L"MISSING") +
          L" bootstrapper=" + (state.bootstrapperPresent ? L"OK" : L"MISSING") +
          L" loader=" + (state.loaderPresent ? L"OK" : L"MISSING") +
          L" mod=" + (state.modAssemblyPresent ? L"OK" : L"MISSING") +
          L" hooks=" + (state.hooksAssemblyPresent ? L"OK" : L"MISSING") +
          L" runtime=" + (state.runtimePresent ? L"OK" : L"MISSING"));
    return state;
}

static void AppendMissingFiles(const fs::path& directory,
                               const TargetState& state,
                               std::wstring& missing) {
    const auto& target = *state.definition;
    const std::wstring architecture = target.loaderArchitecture;
    const std::wstring modArchitecture = target.modArchitecture;
    if (!state.launcherPresent)
        missing += L"  - " + std::wstring(target.launcherName) + L"\n";
    if (!state.bootstrapperPresent)
        missing += L"  - Reloaded-II\\Loader\\" + architecture +
                   L"\\Bootstrapper\\Reloaded.Mod.Loader.Bootstrapper.dll\n";
    if (!state.loaderPresent)
        missing += L"  - Reloaded-II\\Loader\\" + architecture +
                   L"\\Reloaded.Mod.Loader.dll\n";
    if (!state.modConfigPresent)
        missing += L"  - Reloaded-II\\Mods\\ff7.accessibility.reloaded\\ModConfig.json\n";
    if (!state.modAssemblyPresent)
        missing += L"  - Reloaded-II\\Mods\\ff7.accessibility.reloaded\\" +
                   modArchitecture + L"\\" + target.modAssembly + L"\n";
    if (!state.hooksConfigPresent)
        missing += L"  - Reloaded-II\\Mods\\reloaded.sharedlib.hooks\\ModConfig.json\n";
    if (!state.hooksAssemblyPresent)
        missing += L"  - Reloaded-II\\Mods\\reloaded.sharedlib.hooks\\" +
                   modArchitecture + L"\\Reloaded.Hooks.ReloadedII.dll\n";
}

static int RunInstall(Logger& log) {
    fs::path directory = SelfDir();
    log.A("=== Install mode ===");
    TargetState x86 = CheckTarget(directory, X86_TARGET, log);
    TargetState x64 = CheckTarget(directory, X64_TARGET, log);
    if (!x86.gamePresent && !x64.gamePresent) {
        ShowError(L"This installer must be run from the Final Fantasy VII "
                  L"Steam installation folder after all files from the Blind "
                  L"Soldier ZIP have been extracted there.\n\n"
                  L"Neither supported game was found. Expected one of:\n"
                  L"  - FFVII.exe\n"
                  L"  - ff7_en.exe\n"
                  L"  - ff7\\workingdir\\ff7_en.exe\n\n"
                  L"Folder checked:\n" + directory.wstring() +
                  L"\n\nLog: " + log.Path().wstring());
        return 1;
    }

    std::wstring missing;
    if (x86.gamePresent && !x86.RequiredFilesPresent())
        AppendMissingFiles(directory, x86, missing);
    if (x64.gamePresent && !x64.RequiredFilesPresent())
        AppendMissingFiles(directory, x64, missing);
    if (!missing.empty()) {
        ShowError(L"Blind Soldier files are incomplete for the detected game "
                  L"version(s). Extract the complete ZIP into this folder and "
                  L"run the installer again.\n\nMissing files:\n" + missing +
                  L"\nFolder checked:\n" + directory.wstring() +
                  L"\n\nLog: " + log.Path().wstring());
        return 1;
    }

    std::wstring missingRuntimes;
    if (x86.gamePresent && !x86.runtimePresent)
        missingRuntimes += L"  - x86 Microsoft .NET Desktop Runtime 9.0.8 or newer 9.0 patch\n";
    if (x64.gamePresent && !x64.runtimePresent)
        missingRuntimes += L"  - x64 Microsoft .NET Desktop Runtime 9.0.8 or newer 9.0 patch\n";
    if (!missingRuntimes.empty()) {
        ShowError(L"Blind Soldier cannot be registered because a required "
                  L"Microsoft.WindowsDesktop.App runtime is missing:\n\n" +
                  missingRuntimes +
                  L"\nInstall the matching .NET 9 Desktop Runtime from "
                  L"https://dotnet.microsoft.com/download/dotnet/9.0, then "
                  L"run this installer again. No registry changes were made."
                  L"\n\nLog: " + log.Path().wstring());
        return 1;
    }

    std::wstring detected;
    if (x86.gamePresent) detected += L"  - Legacy Steam x86 (ff7_en.exe)\n";
    if (x64.gamePresent) detected += L"  - Steam 2026 x64 (FFVII.exe)\n";
    if (!AskYesNo(L"Install Blind Soldier for the detected Final Fantasy VII "
                  L"version(s)?\n\n" + detected +
                  L"\nThis adds Windows registry entries so ordinary Steam or "
                  L"launcher starts load the accessibility mod automatically.\n\n"
                  L"Location: " + directory.wstring() + L"\n\nProceed?")) {
        log.A("RunInstall: user cancelled at confirmation dialog");
        return 0;
    }

    std::vector<const TargetState*> installed;
    for (const TargetState* state : {&x86, &x64}) {
        if (!state->gamePresent) continue;
        fs::path launcher = directory / state->definition->launcherName;
        bool created = false;
        LONG result = SetIFEODebugger(state->definition->executableName,
                                      launcher.wstring(), created, log);
        if (result != ERROR_SUCCESS) {
            for (const TargetState* prior : installed)
                RemoveIFEODebugger(
                    prior->definition->executableName,
                    (directory / prior->definition->launcherName).wstring(),
                    log);
            ShowError(L"Could not write the Blind Soldier Windows registry "
                      L"entry.\n\nError code: " + std::to_wstring(result) +
                      L" (" + Logger::FormatWin32Error(result) +
                      L")\n\nLog: " + log.Path().wstring());
            return 1;
        }
        if (created) installed.push_back(state);
    }

    ShowInfo(L"Blind Soldier installed successfully.\n\nLaunch Final "
             L"Fantasy VII from Steam or its launcher as usual. The "
             L"accessibility mod will load automatically.\n\nTo uninstall, "
             L"run Blind-Soldier-Installer.exe with /uninstall.");
    log.A("RunInstall: complete");
    return 0;
}

static int RunUninstall(Logger& log) {
    log.A("=== Uninstall mode ===");
    if (!AskYesNo(L"Remove the Blind Soldier automatic-launch registry "
                  L"entries?\n\nThis disables automatic mod loading. It does "
                  L"not delete any extracted files.")) {
        log.A("RunUninstall: user cancelled");
        return 0;
    }

    fs::path directory = SelfDir();
    LONG x86Result = RemoveIFEODebugger(
        GAME_X86_EXE_NAME, (directory / LAUNCHER_X86_NAME).wstring(), log);
    LONG x64Result = RemoveIFEODebugger(
        GAME_X64_EXE_NAME, (directory / LAUNCHER_X64_NAME).wstring(), log);
    LONG result = x86Result != ERROR_SUCCESS ? x86Result : x64Result;
    if (result != ERROR_SUCCESS) {
        ShowError(L"Could not remove every Blind Soldier registry entry.\n\n"
                  L"Error code: " + std::to_wstring(result) + L" (" +
                  Logger::FormatWin32Error(result) + L")\n\nLog: " +
                  log.Path().wstring());
        return 1;
    }
    ShowInfo(L"Blind Soldier automatic loading is disabled.\n\nThe game will "
             L"now launch without this portable mod path. Extracted files "
             L"were left in place and may be deleted manually.");
    log.A("RunUninstall: complete");
    return 0;
}

#ifndef BLIND_SOLDIER_NATIVE_TESTS
int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    int returnCode = 1;
    Logger log;
    try {
        log.Open(SelfDir(), LOG_NAME);
        log.A("=== Blind Soldier Installer start ===");
        log.W(L"selfPath=" + SelfPath().wstring());
        log.W(L"CommandLine=" + std::wstring(GetCommandLineW()));

        int argumentCount = 0;
        LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
        bool uninstall = false;
        if (arguments) {
            for (int index = 1; index < argumentCount; ++index) {
                std::wstring argument = ToLower(arguments[index]);
                if (argument == L"/uninstall" || argument == L"-uninstall" ||
                    argument == L"--uninstall") {
                    uninstall = true;
                }
            }
            LocalFree(arguments);
        }
        log.W(L"mode=" + std::wstring(uninstall ? L"uninstall" : L"install"));

        if (!IsElevated()) {
            returnCode = RelaunchElevated(uninstall ? L"/uninstall" : L"", log)
                             ? 0
                             : 1;
        } else {
            returnCode = uninstall ? RunUninstall(log) : RunInstall(log);
        }
    } catch (const std::exception& error) {
        log.W(L"wWinMain: std::exception: " + Utf8ToWide(error.what()));
    } catch (...) {
        log.A("wWinMain: unknown exception");
    }
    log.W(L"wWinMain: rc=" + std::to_wstring(returnCode));
    log.Close();
    CoUninitialize();
    return returnCode;
}
#endif
