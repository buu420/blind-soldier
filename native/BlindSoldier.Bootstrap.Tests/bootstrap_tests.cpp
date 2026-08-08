#include "../BlindSoldier.Bootstrap/bootstrap_contract.h"
#include "../BlindSoldier.Bootstrap/process_bootstrap.h"
#include "../BlindSoldier.Bootstrap/reloaded_session.h"

#include <atomic>
#include <cstdlib>
#include <memory>
#include <thread>

using namespace blind_soldier;

[[noreturn]] static void CheckFailed(const wchar_t* expression,
                                     const char* file, int line) {
    fwprintf(stderr, L"CHECK failed at %hs:%d: %ls\n", file, line,
             expression);
    ExitProcess(100);
}

#define CHECK(expression) \
    do { if (!(expression)) CheckFailed(L#expression, __FILE__, __LINE__); } while (0)

static const wchar_t* kLaunchId =
    L"01234567-89ab-cdef-0123-456789abcdef";

static int RunDelayedModuleChild(int argumentCount, wchar_t** arguments) {
    if (argumentCount != 5) return 110;
    HANDLE load = OpenEventW(SYNCHRONIZE, FALSE, arguments[2]);
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, arguments[3]);
    HANDLE release = OpenEventW(SYNCHRONIZE, FALSE, arguments[4]);
    if (!load || !ready || !release) {
        if (load) CloseHandle(load);
        if (ready) CloseHandle(ready);
        if (release) CloseHandle(release);
        return 111;
    }
    if (!SetEvent(ready) || WaitForSingleObject(load, 5000) != WAIT_OBJECT_0) {
        CloseHandle(release);
        CloseHandle(ready);
        CloseHandle(load);
        return 112;
    }
    Sleep(75);
    HMODULE module = LoadLibraryW(L"winhttp.dll");
    if (!module) {
        CloseHandle(release);
        CloseHandle(ready);
        CloseHandle(load);
        return 113;
    }
    DWORD wait = WaitForSingleObject(release, 5000);
    FreeLibrary(module);
    CloseHandle(release);
    CloseHandle(ready);
    CloseHandle(load);
    return wait == WAIT_OBJECT_0 ? 0 : 114;
}

static fs::path NewTestRoot(const wchar_t* suffix) {
    fs::path root = fs::temp_directory_path() /
        (L"blind-soldier-bootstrap-tests-" +
         std::to_wstring(GetCurrentProcessId()) + L"-" + suffix + L"-" +
         std::to_wstring(GetTickCount64()));
    std::error_code error;
    fs::remove_all(root, error);
    fs::create_directories(root);
    return root;
}

static std::string ReadRequired(const fs::path& path) {
    std::string value;
    CHECK(ReadUtf8File(path, value));
    return value;
}

static void Touch(const fs::path& path, const std::string& value = "fixture") {
    fs::create_directories(path.parent_path());
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output.write(value.data(), static_cast<std::streamsize>(value.size()));
    CHECK(output.good());
}

static void CopySelf(const fs::path& path) {
    fs::create_directories(path.parent_path());
    std::error_code error;
    fs::copy_file(SelfPath(), path, fs::copy_options::overwrite_existing,
                  error);
    CHECK(!error);
}

static BootstrapRequest Parse(const std::vector<std::wstring>& arguments) {
    BootstrapRequest request{};
    std::wstring error;
    CHECK(TryParseBootstrapRequest(arguments, request, error));
    CHECK(error.empty());
    return request;
}

static void CheckParseFailures() {
    BootstrapRequest request{};
    std::wstring error;
    CHECK(!TryParseBootstrapRequest({}, request, error));
    CHECK(!error.empty());
    CHECK(!TryParseBootstrapRequest(
        {L"--launch", L"--attach", L"--root", L"C:\\Game",
         L"--game", L"C:\\Game\\FFVII.exe", L"--launch-id", kLaunchId},
        request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--launch", L"--root", L"C:\\Game", L"--root", L"C:\\Other",
         L"--game", L"C:\\Game\\FFVII.exe", L"--launch-id", kLaunchId},
        request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--launch", L"--root"}, request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--attach", L"--root", L"C:\\Game", L"--game",
         L"C:\\Game\\ff7.exe", L"--pid", L"zero", L"--ready-event",
         L"Local\\BlindSoldier.Ready.test", L"--launch-id", kLaunchId},
        request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--attach", L"--root", L"C:\\Game", L"--game",
         L"C:\\Game\\ff7.exe", L"--pid", L"12", L"--ready-event",
         L"Global\\wrong", L"--launch-id", kLaunchId}, request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--launch", L"--root", L"C:\\Game", L"--game",
         L"C:\\Game\\FFVII.exe", L"--launch-id", L"not-a-guid"},
        request, error));
    CHECK(!TryParseBootstrapRequest(
        {L"--launch", L"--root", L"C:\\Game", L"--game",
         L"C:\\Game\\FFVII.exe", L"--launch-id", kLaunchId,
         L"--unknown", L"value"}, request, error));
}

static void CheckParserPreservesQuotedValues() {
    auto launch = Parse({
        L"--launch", L"--root", L"C:\\Games With Spaces\\FF7",
        L"--game", L"C:\\Games With Spaces\\FF7\\FFVII.exe",
        L"--game-arguments", L"jp", L"--launch-id", kLaunchId});
    CHECK(launch.mode == BootstrapMode::Launch);
    CHECK(launch.packageRoot == L"C:\\Games With Spaces\\FF7");
    CHECK(launch.gameExecutable ==
          L"C:\\Games With Spaces\\FF7\\FFVII.exe");
    CHECK(launch.gameArguments == L"jp");
    CHECK(launch.processId == 0);

    auto attach = Parse({
        L"--attach", L"--root", L"C:\\Games\\FF7", L"--game",
        L"C:\\Games\\FF7\\ff7.exe", L"--pid", L"1234",
        L"--ready-event", L"Local\\BlindSoldier.Ready.01234567",
        L"--launch-id", kLaunchId});
    CHECK(attach.mode == BootstrapMode::Attach);
    CHECK(attach.processId == 1234);
    CHECK(attach.readyEventName ==
          L"Local\\BlindSoldier.Ready.01234567");
}

static void CheckLeaseLifecycle() {
    fs::path root = NewTestRoot(L"lease");
    fs::path pointer = root / L"ReloadedII.json";
    fs::path backup = pointer.wstring() + L".blind_soldier_backup";
    Logger log;
    log.Open(root, L"lease.log");

    // No prior pointer.
    {
        ReloadedPointerLease lease(root / L"portable", log, 20000, pointer);
        CHECK(lease.Ready());
        CHECK(fs::exists(pointer));
    }
    CHECK(!fs::exists(pointer));

    // Prior pointer is restored after normal target exit.
    CHECK(WriteUtf8FileAtomic(pointer, L"original"));
    {
        ReloadedPointerLease lease(root / L"portable", log, 20000, pointer);
        CHECK(lease.Ready());
        CHECK(fs::exists(backup));
    }
    CHECK(ReadRequired(pointer) == "original");
    CHECK(!fs::exists(backup));

    // A stale owned pointer and backup recover after a simulated target crash.
    CHECK(WriteUtf8FileAtomic(backup, L"original"));
    CHECK(WriteReloadedIIPointerAt(pointer, root / L"portable", log));
    {
        ReloadedPointerLease lease(root / L"portable", log, 20000, pointer);
        CHECK(lease.Ready());
    }
    CHECK(ReadRequired(pointer) == "original");
    CHECK(!fs::exists(backup));

    // An external change is never overwritten.
    {
        ReloadedPointerLease lease(root / L"portable", log, 20000, pointer);
        CHECK(lease.Ready());
        CHECK(WriteUtf8FileAtomic(pointer, L"external"));
    }
    CHECK(ReadRequired(pointer) == "external");
    CHECK(ReadRequired(backup) == "original");
    {
        ReloadedPointerLease lease(root / L"portable", log, 5, pointer);
        CHECK(!lease.Ready());
        CHECK(!lease.Diagnostic().empty());
    }

    log.Close();
    fs::remove_all(root);
}

static void CheckLoggerAppendsWithOneBom() {
    fs::path root = NewTestRoot(L"logger");
    {
        Logger log;
        log.Open(root, L"append.log");
        log.A("first session");
        log.Close();
    }
    {
        Logger log;
        log.Open(root, L"append.log");
        log.A("second session");
        log.Close();
    }
    std::string content = ReadRequired(root / L"append.log");
    CHECK(content.size() > 3);
    CHECK(static_cast<unsigned char>(content[0]) == 0xEF);
    CHECK(static_cast<unsigned char>(content[1]) == 0xBB);
    CHECK(static_cast<unsigned char>(content[2]) == 0xBF);
    CHECK(content.find("first session") != std::string::npos);
    CHECK(content.find("second session") != std::string::npos);
    CHECK(content.find("\xEF\xBB\xBF", 3) == std::string::npos);
    fs::remove_all(root);
}

static void CheckLeaseTimeoutAndAcquisitionAfterRelease() {
    fs::path root = NewTestRoot(L"mutex");
    fs::path pointer = root / L"ReloadedII.json";
    Logger firstLog;
    firstLog.Open(root, L"first.log");
    auto first = std::make_unique<ReloadedPointerLease>(
        root / L"portable", firstLog, 20000, pointer);
    CHECK(first->Ready());

    std::atomic<bool> timedOut = false;
    std::thread waiter([&]() {
        Logger secondLog;
        secondLog.Open(root, L"second.log");
        ReloadedPointerLease second(root / L"portable", secondLog, 30,
                                    pointer);
        timedOut = !second.Ready();
        secondLog.Close();
    });
    waiter.join();
    CHECK(timedOut);
    first.reset();

    Logger thirdLog;
    thirdLog.Open(root, L"third.log");
    {
        ReloadedPointerLease third(root / L"portable", thirdLog, 20000,
                                   pointer);
        CHECK(third.Ready());
    }
    thirdLog.Close();
    firstLog.Close();
    fs::remove_all(root);
}

static void PopulatePayload(const fs::path& root,
                            ExpectedHostArchitecture architecture) {
    const wchar_t* loaderArch = architecture == ExpectedHostArchitecture::X86
        ? L"X86" : L"X64";
    const wchar_t* modArch = architecture == ExpectedHostArchitecture::X86
        ? L"x86" : L"x64";
    const wchar_t* modAssembly = architecture == ExpectedHostArchitecture::X86
        ? L"Ff7.Accessibility.Reloaded.dll"
        : L"Ff7.Accessibility.Steam2026X64.dll";
    fs::path reloaded = root / L"Reloaded-II";
    CopySelf(reloaded / L"Loader" / loaderArch / L"Bootstrapper" /
             L"Reloaded.Mod.Loader.Bootstrapper.dll");
    CopySelf(reloaded / L"Loader" / loaderArch /
             L"Reloaded.Mod.Loader.dll");
    Touch(reloaded / L"portable.txt", "");
    Touch(reloaded / L"Mods" / ACCESSIBILITY_MOD_ID / L"ModConfig.json",
          "{}");
    Touch(reloaded / L"Mods" / SHARED_HOOKS_MOD_ID / L"ModConfig.json",
          "{}");
    CopySelf(reloaded / L"Mods" / ACCESSIBILITY_MOD_ID / modArch /
             modAssembly);
    CopySelf(reloaded / L"Mods" / ACCESSIBILITY_MOD_ID / modArch /
             L"prism.dll");
    CopySelf(reloaded / L"Mods" / SHARED_HOOKS_MOD_ID / modArch /
             L"Reloaded.Hooks.ReloadedII.dll");
    CopySelf(root / L"Blind-Soldier" / L"Runtime" / L"dotnet" / modArch /
             L"host" / L"fxr" / L"9.0.8" / L"hostfxr.dll");
}

static void CheckPayloadValidation() {
    fs::path root = NewTestRoot(L"payload");
#ifdef _WIN64
    constexpr auto architecture = ExpectedHostArchitecture::X64;
    const wchar_t* gameName = L"FFVII.exe";
#else
    constexpr auto architecture = ExpectedHostArchitecture::X86;
    const wchar_t* gameName = L"ff7.exe";
#endif
    PopulatePayload(root, architecture);
    fs::path game = root / gameName;
    CopySelf(game);
    BootstrapRequest request{};
    request.packageRoot = root;
    request.gameExecutable = game;
    Logger log;
    log.Open(root / L"Blind-Soldier" / L"Logs", L"payload.log");
    ValidatedPayload payload;
    CHECK(ValidatePortablePayload(request, architecture, payload, log));
    CHECK(fs::exists(payload.bootstrapper));
    CHECK(fs::exists(payload.privateRuntimeRoot / L"host" / L"fxr" /
                     L"9.0.8" / L"hostfxr.dll"));
    auto appConfig = root / L"Reloaded-II" / L"Apps" /
        ToLower(fs::path(gameName).wstring()) / L"AppConfig.json";
    std::string config = ReadRequired(appConfig);
    CHECK(config.find("reloaded.sharedlib.hooks") <
          config.find("ff7.accessibility.reloaded"));

    fs::remove(payload.prism);
    CHECK(!ValidatePortablePayload(request, architecture, payload, log));
    log.Close();
    fs::remove_all(root);
}

static std::wstring ReadEnvironment(const wchar_t* name) {
    DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
    if (required == 0) return {};
    std::vector<wchar_t> buffer(required);
    DWORD copied = GetEnvironmentVariableW(name, buffer.data(), required);
    return copied > 0 && copied < required
        ? std::wstring(buffer.data(), copied) : std::wstring();
}

static void CheckPrivateDotNetEnvironment() {
    fs::path root = NewTestRoot(L"dotnet-environment");
#ifdef _WIN64
    constexpr auto architecture = ExpectedHostArchitecture::X64;
    const wchar_t* architectureVariable = L"DOTNET_ROOT_X64";
#else
    constexpr auto architecture = ExpectedHostArchitecture::X86;
    const wchar_t* architectureVariable = L"DOTNET_ROOT_X86";
#endif
    CopySelf(root / L"host" / L"fxr" / L"9.0.8" / L"hostfxr.dll");
    std::error_code canonicalError;
    const fs::path canonicalRoot = fs::canonical(root, canonicalError);
    CHECK(!canonicalError);
    Logger log;
    log.Open(root, L"environment.log");
    CHECK(ApplyPrivateDotNetEnvironment(architecture, root, log));
    CHECK(ReadEnvironment(L"DOTNET_ROOT") == canonicalRoot.wstring());
    CHECK(ReadEnvironment(architectureVariable) == canonicalRoot.wstring());
#ifndef _WIN64
    CHECK(ReadEnvironment(L"DOTNET_ROOT(x86)") == canonicalRoot.wstring());
#endif
    CHECK(!ApplyPrivateDotNetEnvironment(
        architecture, root / L"missing", log));
    SetEnvironmentVariableW(L"DOTNET_ROOT", nullptr);
    SetEnvironmentVariableW(architectureVariable, nullptr);
#ifndef _WIN64
    SetEnvironmentVariableW(L"DOTNET_ROOT(x86)", nullptr);
#endif
    log.Close();
    fs::remove_all(root);
}

static void CheckRunBoundaryRejectsInvalidArchitectureAndEscapes() {
    fs::path root = NewTestRoot(L"boundary");
    Logger log;
    log.Open(root, L"boundary.log");
    BootstrapRequest request{};
    request.packageRoot = root;
    request.gameExecutable = root.parent_path() / L"outside.exe";
    request.launchId = kLaunchId;
#ifdef _WIN64
    request.mode = BootstrapMode::Launch;
#else
    request.mode = BootstrapMode::Attach;
    request.processId = GetCurrentProcessId();
    request.readyEventName = L"Local\\BlindSoldier.Ready.boundary";
#endif
    CHECK(RunBootstrap(request, log) == BootstrapExitCode::InvalidArguments);

#ifdef _WIN64
    request.mode = BootstrapMode::Attach;
#else
    request.mode = BootstrapMode::Launch;
#endif
    request.gameExecutable = root / L"game.exe";
    CHECK(RunBootstrap(request, log) ==
          BootstrapExitCode::ArchitectureMismatch);
    log.Close();
    fs::remove_all(root);
}

static void CheckPidPathDisagreement() {
#ifndef _WIN64
    fs::path root = NewTestRoot(L"pid-path");
    fs::path claimedGame = root / L"ff7.exe";
    CopySelf(claimedGame);
    BootstrapRequest request{};
    request.mode = BootstrapMode::Attach;
    request.packageRoot = root;
    request.gameExecutable = claimedGame;
    request.processId = GetCurrentProcessId();
    request.readyEventName = L"Local\\BlindSoldier.Ready.pid-path";
    request.launchId = kLaunchId;
    Logger log;
    log.Open(root, L"pid-path.log");
    CHECK(RunBootstrap(request, log) == BootstrapExitCode::TargetUnavailable);
    log.Close();
    fs::remove_all(root);
#endif
}

static void CheckDelayedModuleReadiness() {
    fs::path root = NewTestRoot(L"module-readiness");
    Logger log;
    log.Open(root, L"module-readiness.log");
    const std::wstring suffix = std::to_wstring(GetCurrentProcessId()) +
        L"." + std::to_wstring(GetTickCount64());
    const std::wstring loadName =
        L"Local\\BlindSoldier.Bootstrap.Tests.Load." + suffix;
    const std::wstring readyName =
        L"Local\\BlindSoldier.Bootstrap.Tests.Ready." + suffix;
    const std::wstring releaseName =
        L"Local\\BlindSoldier.Bootstrap.Tests.Release." + suffix;
    HANDLE load = CreateEventW(nullptr, TRUE, FALSE, loadName.c_str());
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, readyName.c_str());
    HANDLE release = CreateEventW(nullptr, TRUE, FALSE, releaseName.c_str());
    CHECK(load != nullptr);
    CHECK(ready != nullptr);
    CHECK(release != nullptr);

    const fs::path self = SelfPath();
    std::wstring command = L"\"" + self.wstring() +
        L"\" --delayed-module-child \"" + loadName + L"\" \"" +
        readyName + L"\" \"" + releaseName + L"\"";
    std::vector<wchar_t> commandLine(command.begin(), command.end());
    commandLine.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION child{};
    CHECK(CreateProcessW(self.c_str(), commandLine.data(), nullptr, nullptr,
                         FALSE, CREATE_NO_WINDOW, nullptr,
                         self.parent_path().c_str(), &startup, &child));
    CloseHandle(child.hThread);
    CHECK(WaitForSingleObject(ready, 5000) == WAIT_OBJECT_0);

    LPVOID beforeLoad = WaitForRemoteModuleBase(
        child.hProcess, child.dwProcessId, L"winhttp.dll", 0, log);
    std::thread trigger([load]() {
        Sleep(100);
        SetEvent(load);
    });
    LPVOID afterLoad = WaitForRemoteModuleBase(
        child.hProcess, child.dwProcessId, L"winhttp.dll", 2000, log);
    trigger.join();
    SetEvent(release);
    DWORD childWait = WaitForSingleObject(child.hProcess, 5000);
    DWORD childExit = STILL_ACTIVE;
    if (childWait == WAIT_OBJECT_0)
        GetExitCodeProcess(child.hProcess, &childExit);
    CloseHandle(child.hProcess);
    CloseHandle(release);
    CloseHandle(ready);
    CloseHandle(load);
    log.Close();
    fs::remove_all(root);

    CHECK(beforeLoad == nullptr);
    CHECK(afterLoad != nullptr);
    CHECK(childWait == WAIT_OBJECT_0);
    CHECK(childExit == 0);
}

int wmain(int argumentCount, wchar_t** arguments) {
    if (argumentCount > 1 &&
        wcscmp(arguments[1], L"--prove-check-failure") == 0) {
        CHECK(false);
    }
    if (argumentCount > 1 &&
        wcscmp(arguments[1], L"--delayed-module-child") == 0) {
        return RunDelayedModuleChild(argumentCount, arguments);
    }
    CheckParseFailures();
    CheckParserPreservesQuotedValues();
    CheckLeaseLifecycle();
    CheckLoggerAppendsWithOneBom();
    CheckLeaseTimeoutAndAcquisitionAfterRelease();
    CheckPayloadValidation();
    CheckPrivateDotNetEnvironment();
    CheckRunBoundaryRejectsInvalidArchitectureAndEscapes();
    CheckPidPathDisagreement();
    CheckDelayedModuleReadiness();
    fwprintf(stdout, L"Blind Soldier bootstrap tests passed.\n");
    return 0;
}
