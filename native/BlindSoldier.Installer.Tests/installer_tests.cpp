#define BLIND_SOLDIER_NATIVE_TESTS
#include "../BlindSoldier.Installer/installer.cpp"

#include <cstdlib>

[[noreturn]] static void CheckFailed(const wchar_t* expression,
                                     const char* file, int line) {
    fwprintf(stderr, L"CHECK failed at %hs:%d: %ls\n", file, line,
             expression);
    ExitProcess(100);
}

#define CHECK(expression) \
    do { if (!(expression)) CheckFailed(L#expression, __FILE__, __LINE__); } while (0)

static fs::path NewTestRoot() {
    fs::path root = fs::temp_directory_path() /
        (L"blind-soldier-installer-tests-" +
         std::to_wstring(GetCurrentProcessId()));
    std::error_code error;
    fs::remove_all(root, error);
    fs::create_directories(root);
    return root;
}

static void CreateRuntime(const fs::path& root, const wchar_t* version,
                          bool desktop, bool core) {
    if (desktop)
        fs::create_directories(root / L"shared" /
                               L"Microsoft.WindowsDesktop.App" / version);
    if (core)
        fs::create_directories(root / L"shared" /
                               L"Microsoft.NETCore.App" / version);
}

int wmain(int argumentCount, wchar_t** arguments) {
    if (argumentCount > 1 &&
        wcscmp(arguments[1], L"--prove-check-failure") == 0) {
        CHECK(false);
    }
    fs::path root = NewTestRoot();
    Logger log;
    log.Open(root, L"installer-tests.log");

    fs::path runtime = root / L"dotnet";
    CreateRuntime(runtime, L"9.0.7", true, true);
    CHECK(!HasCompatibleDesktopRuntimeAtRoot(runtime));
    CreateRuntime(runtime, L"9.0.8", true, false);
    CHECK(!HasCompatibleDesktopRuntimeAtRoot(runtime));
    CreateRuntime(runtime, L"9.0.8", false, true);
    CHECK(HasCompatibleDesktopRuntimeAtRoot(runtime));

    gIfeoHive = HKEY_CURRENT_USER;
    gIfeoRoot = L"Software\\BlindSoldierNativeTests\\" +
        std::to_wstring(GetCurrentProcessId());
    const std::wstring target = L"ff7-test.exe";
    const std::wstring launcherA = (root / L"launcher-a.exe").wstring();
    const std::wstring launcherB = (root / L"launcher-b.exe").wstring();
    const std::wstring expectedA = L"\"" + launcherA + L"\"";

    bool created = false;
    CHECK(SetIFEODebugger(target, launcherA, created, log) == ERROR_SUCCESS);
    CHECK(created);
    CHECK(SetIFEODebugger(target, launcherA, created, log) == ERROR_SUCCESS);
    CHECK(!created);
    CHECK(SetIFEODebugger(target, launcherB, created, log) ==
          ERROR_ALREADY_ASSIGNED);
    CHECK(!created);
    CHECK(RemoveIFEODebugger(target, launcherB, log) ==
          ERROR_ALREADY_ASSIGNED);

    HKEY key = nullptr;
    std::wstring keyPath = gIfeoRoot + L"\\" + target;
    CHECK(RegOpenKeyExW(HKEY_CURRENT_USER, keyPath.c_str(), 0,
                         KEY_QUERY_VALUE | KEY_SET_VALUE, &key) ==
          ERROR_SUCCESS);
    std::wstring debugger;
    std::wstring owner;
    bool debuggerExists = false;
    bool ownerExists = false;
    CHECK(ReadRegistryString(key, L"Debugger", debugger, debuggerExists) ==
          ERROR_SUCCESS);
    CHECK(ReadRegistryString(key, IFEO_OWNER_VALUE, owner, ownerExists) ==
          ERROR_SUCCESS);
    CHECK(debuggerExists && ownerExists);
    CHECK(debugger == expectedA && owner == expectedA);
    RegCloseKey(key);

    CHECK(RemoveIFEODebugger(target, launcherA, log) == ERROR_SUCCESS);

    const std::wstring foreignTarget = L"foreign.exe";
    std::wstring foreignKeyPath = gIfeoRoot + L"\\" + foreignTarget;
    CHECK(RegCreateKeyExW(HKEY_CURRENT_USER, foreignKeyPath.c_str(), 0,
                           nullptr, REG_OPTION_NON_VOLATILE,
                           KEY_QUERY_VALUE | KEY_SET_VALUE, nullptr, &key,
                           nullptr) == ERROR_SUCCESS);
    CHECK(WriteRegistryString(key, L"Debugger", L"\"foreign.exe\"") ==
          ERROR_SUCCESS);
    RegCloseKey(key);
    CHECK(SetIFEODebugger(foreignTarget, launcherA, created, log) ==
          ERROR_ALREADY_ASSIGNED);
    CHECK(!created);
    CHECK(RemoveIFEODebugger(foreignTarget, launcherA, log) ==
          ERROR_ALREADY_ASSIGNED);
    CHECK(RegOpenKeyExW(HKEY_CURRENT_USER, foreignKeyPath.c_str(), 0,
                        KEY_QUERY_VALUE, &key) == ERROR_SUCCESS);
    CHECK(ReadRegistryString(key, L"Debugger", debugger, debuggerExists) ==
          ERROR_SUCCESS);
    CHECK(debuggerExists && debugger == L"\"foreign.exe\"");
    RegCloseKey(key);

    RegDeleteTreeW(HKEY_CURRENT_USER, gIfeoRoot.c_str());
    log.Close();
    fs::remove_all(root);
    return 0;
}
