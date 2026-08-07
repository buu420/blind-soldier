#include "proxy_state.h"

#include "../BlindSoldier.Common/pe_image.h"

#include <algorithm>
#include <array>
#include <cwctype>
#include <memory>
#include <vector>

#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
extern "C" FARPROC g_winmmExports[blind_soldier::kWinmmExportCount] = {};
#endif

namespace blind_soldier {
namespace {

HMODULE g_proxyModule = nullptr;
#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
bool g_loadWinmmForForwarding = true;
#endif
bool g_requireStockRuntimeReadiness = false;
std::function<StockRuntimeReadinessResult(
    const fs::path&, const HostValidationResult&, Logger&)>
    g_waitForStockRuntime;
std::wstring g_bootstrapComponent = L"WinMM";
HANDLE g_workerFinished = nullptr;
volatile LONG g_proxyState = static_cast<LONG>(ProxyBootstrapState::Pending);
volatile LONG g_failureShown = 0;
SRWLOCK g_failureLock = SRWLOCK_INIT;
std::wstring g_failureMessage;
fs::path g_proxyLogPath;
Logger g_proxyLog;

bool IsOrdinaryPath(const fs::path& path, bool directory) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES ||
        (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        return false;
    }
    return directory
        ? (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0
        : (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool Canonicalize(const fs::path& path, fs::path& result) {
    std::error_code error;
    result = fs::weakly_canonical(path, error);
    return !error && !result.empty();
}

bool IsWithin(const fs::path& root, const fs::path& candidate) {
    fs::path canonicalRoot;
    fs::path canonicalCandidate;
    if (!Canonicalize(root, canonicalRoot) ||
        !Canonicalize(candidate, canonicalCandidate)) {
        return false;
    }
    auto rootIt = canonicalRoot.begin();
    auto candidateIt = canonicalCandidate.begin();
    for (; rootIt != canonicalRoot.end(); ++rootIt, ++candidateIt) {
        if (candidateIt == canonicalCandidate.end() ||
            _wcsicmp(rootIt->c_str(), candidateIt->c_str()) != 0) {
            return false;
        }
    }
    return true;
}

std::wstring ModulePath(HMODULE module) {
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(module, buffer.data(),
                                            static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return {};
    return std::wstring(buffer.data(), length);
}

std::wstring NewLaunchId() {
    GUID guid{};
    if (FAILED(CoCreateGuid(&guid))) return {};
    wchar_t buffer[64]{};
    if (StringFromGUID2(guid, buffer, ARRAYSIZE(buffer)) <= 0) return {};
    std::wstring result(buffer);
    if (result.size() >= 2 && result.front() == L'{' &&
        result.back() == L'}') {
        result = result.substr(1, result.size() - 2);
    }
    return result;
}

void CompleteWorker(ProxyBootstrapState state,
                    const std::wstring& diagnostic = {}) {
    AcquireSRWLockExclusive(&g_failureLock);
    g_failureMessage = diagnostic;
    ReleaseSRWLockExclusive(&g_failureLock);
    InterlockedExchange(&g_proxyState, static_cast<LONG>(state));
    if (g_workerFinished) SetEvent(g_workerFinished);
}

void SetFailureMessage(const std::wstring& message) {
    AcquireSRWLockExclusive(&g_failureLock);
    g_failureMessage = message;
    ReleaseSRWLockExclusive(&g_failureLock);
}

std::wstring FailureMessage() {
    AcquireSRWLockShared(&g_failureLock);
    const std::wstring message = g_failureMessage;
    ReleaseSRWLockShared(&g_failureLock);
    return message;
}

#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
bool LoadCanonicalSystemWinmm(Logger& log) {
    std::vector<wchar_t> directory(32768);
    const UINT length = GetSystemWow64DirectoryW(
        directory.data(), static_cast<UINT>(directory.size()));
    fs::path systemDirectory;
    if (length > 0 && length < directory.size() && directory[0] != L'\0') {
        systemDirectory = fs::path(directory.data(), directory.data() + length);
    }
    else {
        // Some WOW64 builds report the required character count without
        // populating the buffer to an x86 caller. Preserve the explicit API
        // probe, then construct the same canonical directory from the Windows
        // root instead of relying on redirected System32 lookup.
        std::vector<wchar_t> windowsDirectory(32768);
        const UINT windowsLength = GetWindowsDirectoryW(
            windowsDirectory.data(),
            static_cast<UINT>(windowsDirectory.size()));
        if (windowsLength == 0 || windowsLength >= windowsDirectory.size()) {
            log.Err(L"GetSystemWow64DirectoryW/GetWindowsDirectoryW",
                    GetLastError());
            return false;
        }
        systemDirectory = fs::path(windowsDirectory.data(),
                                   windowsDirectory.data() + windowsLength) /
            L"SysWOW64";
        log.A("GetSystemWow64DirectoryW returned no path; used explicit Windows\\SysWOW64 fallback");
    }
    const fs::path systemWinmm = systemDirectory / L"winmm.dll";
    if (!IsOrdinaryPath(systemWinmm, false)) {
        log.W(L"Canonical SysWOW64 WinMM is unavailable: " +
              systemWinmm.wstring());
        return false;
    }
    const HMODULE module = LoadLibraryW(systemWinmm.c_str());
    if (!module) {
        log.Err(L"LoadLibraryW(canonical SysWOW64 WinMM)", GetLastError());
        return false;
    }
    for (size_t index = 0; index < kWinmmExportCount; ++index) {
        const WORD ordinal = static_cast<WORD>(index + 2);
        g_winmmExports[index] = GetProcAddress(
            module, MAKEINTRESOURCEA(ordinal));
        if (!g_winmmExports[index]) {
            log.W(L"Canonical WinMM is missing ordinal " +
                  std::to_wstring(ordinal));
            return false;
        }
    }
    fs::path canonical;
    if (!Canonicalize(systemWinmm, canonical)) canonical = systemWinmm;
    log.W(L"canonical system WinMM loaded: " + canonical.wstring());
    return true;
}
#endif

ProxyBootstrapHooks DefaultHooks() {
    ProxyBootstrapHooks hooks;
    hooks.isCompleteRoot = IsCompletePortableRoot;
    hooks.validateHost = [](const fs::path& path) {
        return ValidateSupportedHost(path, ExpectedHostArchitecture::X86);
    };
    hooks.waitForStockRuntime = g_waitForStockRuntime;
    hooks.applyPrivateRuntime = ApplyPrivateDotNetEnvironmentForProxy;
    hooks.startBrokerAndWait = StartBrokerAndWaitForReady;
    return hooks;
}

std::wstring FailureText(const std::wstring& cause, const fs::path& logPath) {
    return L"Blind Soldier could not start accessibility.\n\nCause: " + cause +
        L"\n\nAction: Extract every file from the Blind Soldier ZIP into the "
        L"same Final Fantasy VII game folder, then start the game again."
        L"\n\nLog: " + logPath.wstring();
}

DWORD WINAPI ProxyWorker(void*) {
    try {
        const fs::path proxyPath = ModulePath(g_proxyModule);
        const fs::path diagnosticRoot = DeriveDiagnosticRoot(proxyPath);
        const std::wstring launchId = NewLaunchId();
        const std::wstring logName = L"Blind-Soldier-" +
            g_bootstrapComponent + L"-" +
            (launchId.empty() ? std::to_wstring(GetCurrentProcessId())
                              : launchId) + L".log";
        g_proxyLogPath = diagnosticRoot / L"Blind-Soldier" / L"Logs" /
            logName;
        g_proxyLog.Open(g_proxyLogPath.parent_path(), logName.c_str());
        g_proxyLog.W(L"bootstrap module=" + proxyPath.wstring());
#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
        if (g_loadWinmmForForwarding &&
            !LoadCanonicalSystemWinmm(g_proxyLog)) {
            CompleteWorker(ProxyBootstrapState::Failed,
                FailureText(L"The canonical Windows multimedia library could not be loaded.",
                            g_proxyLogPath));
            return 1;
        }
        if (!g_loadWinmmForForwarding) {
            g_proxyLog.W(L"native " + g_bootstrapComponent +
                L" bootstrap active; WinMM forwarding is not required");
        }
#else
        g_proxyLog.W(L"native " + g_bootstrapComponent +
            L" bootstrap active");
#endif

        const fs::path processImage = ModulePath(nullptr);
        if (!IsSupportedFf7ProcessName(processImage)) {
            g_proxyLog.W(L"unrelated process; bootstrap skipped: " +
                         processImage.wstring());
            CompleteWorker(ProxyBootstrapState::ForwardOnly);
            return 0;
        }
        if (launchId.empty()) {
            CompleteWorker(ProxyBootstrapState::Failed,
                FailureText(L"A launch identifier could not be created.",
                            g_proxyLogPath));
            return 1;
        }

        ProxyBootstrapContext context;
        context.processImage = processImage;
        context.proxyModule = proxyPath;
        context.processId = GetCurrentProcessId();
        context.launchId = launchId;
        context.readyEventName = BuildReadyEventName(launchId);
        context.requireStockRuntimeReadiness =
            g_requireStockRuntimeReadiness;
        const ProxyBootstrapOutcome outcome = CoordinateProxyBootstrap(
            context, DefaultHooks(), g_proxyLog);
        if (outcome.state == ProxyBootstrapState::Ready ||
            outcome.state == ProxyBootstrapState::ForwardOnly) {
            CompleteWorker(outcome.state);
            return 0;
        }
        CompleteWorker(outcome.state,
            FailureText(outcome.diagnostic, g_proxyLogPath));
        return 1;
    }
    catch (const std::exception& error) {
        const std::wstring cause = Utf8ToWide(error.what());
        CompleteWorker(ProxyBootstrapState::Failed,
            FailureText(cause.empty() ? L"The proxy encountered an unexpected error."
                                      : cause,
                        g_proxyLogPath));
        return 1;
    }
    catch (...) {
        CompleteWorker(ProxyBootstrapState::Failed,
            FailureText(L"The proxy encountered an unexpected error.",
                        g_proxyLogPath));
        return 1;
    }
}

}  // namespace

bool IsSupportedFf7ProcessName(const fs::path& processImage) {
    const std::wstring name = ToLower(processImage.filename().wstring());
    return name == L"ff7_en.exe" || name == L"ff7.exe";
}

bool IsCompletePortableRoot(const fs::path& candidate) {
    if (!IsOrdinaryPath(candidate, true)) return false;
    static const std::array<const wchar_t*, 5> required = {
        L"Blind-Soldier\\Bootstrap\\x86\\Blind-Soldier-Bootstrap-x86.exe",
        L"Blind-Soldier\\Runtime\\dotnet\\x86\\host\\fxr\\9.0.8\\hostfxr.dll",
        L"Reloaded-II\\portable.txt",
        L"Reloaded-II\\Loader\\X86\\Bootstrapper\\Reloaded.Mod.Loader.Bootstrapper.dll",
        L"Reloaded-II\\Loader\\X86\\Reloaded.Mod.Loader.dll"
    };
    for (const wchar_t* relative : required) {
        if (!IsOrdinaryPath(candidate / relative, false)) return false;
    }
    return true;
}

fs::path DeriveDiagnosticRoot(const fs::path& proxyModule) {
    fs::path localDirectory = proxyModule.parent_path();
    const std::wstring leaf = ToLower(localDirectory.filename().wstring());
    if (leaf.size() < 10 ||
        leaf.compare(leaf.size() - 10, 10, L".exe.local") != 0) {
        if (leaf == L"workingdir" &&
            ToLower(localDirectory.parent_path().filename().wstring()) ==
                L"ff7") {
            return localDirectory.parent_path().parent_path();
        }
        return localDirectory;
    }
    fs::path candidate = localDirectory.parent_path();
    if (ToLower(candidate.filename().wstring()) == L"workingdir" &&
        ToLower(candidate.parent_path().filename().wstring()) == L"ff7") {
        return candidate.parent_path().parent_path();
    }
    return candidate;
}

std::wstring BuildReadyEventName(const std::wstring& launchId) {
    return std::wstring(READY_EVENT_PREFIX) + launchId;
}

bool DiscoverPortableRoot(
    const fs::path& proxyModule,
    const fs::path& processImage,
    const std::function<bool(const fs::path&)>& isCompleteRoot,
    fs::path& packageRoot,
    std::wstring& diagnostic) {
    packageRoot.clear();
    if (!isCompleteRoot) {
        diagnostic = L"The portable-root validator is unavailable.";
        return false;
    }
    fs::path canonicalProxy;
    fs::path canonicalProcess;
    if (!Canonicalize(proxyModule, canonicalProxy) ||
        !Canonicalize(processImage, canonicalProcess)) {
        diagnostic = L"The game or bootstrap module path could not be resolved.";
        return false;
    }
    const fs::path localDirectory = canonicalProxy.parent_path();
    const std::wstring expectedLocal = ToLower(
        canonicalProcess.filename().wstring() + L".local");
    const bool executableLocal =
        ToLower(localDirectory.filename().wstring()) == expectedLocal;
    fs::path canonicalProcessDirectory;
    const bool siblingVersion =
        ToLower(canonicalProxy.filename().wstring()) == L"version.dll" &&
        Canonicalize(canonicalProcess.parent_path(), canonicalProcessDirectory) &&
        _wcsicmp(localDirectory.c_str(), canonicalProcessDirectory.c_str()) == 0;
    if (!executableLocal && !siblingVersion) {
        diagnostic = L"The bootstrap module is neither the executable-specific .local module nor the sibling x86 version.dll.";
        return false;
    }

    std::vector<fs::path> matches;
    fs::path candidate = executableLocal
        ? localDirectory.parent_path()
        : localDirectory;
    for (int depth = 0; depth < kProxyRootSearchDepth && !candidate.empty();
         ++depth) {
        fs::path canonicalCandidate;
        if (Canonicalize(candidate, canonicalCandidate) &&
            isCompleteRoot(canonicalCandidate)) {
            matches.push_back(canonicalCandidate);
        }
        candidate = candidate.parent_path();
    }
    if (matches.empty()) {
        diagnostic = L"No complete Blind Soldier package root was found within four parent directories.";
        return false;
    }
    if (matches.size() != 1) {
        diagnostic = L"More than one complete Blind Soldier package root was found.";
        return false;
    }
    if (!IsWithin(matches.front(), canonicalProcess)) {
        diagnostic = L"The running FFVII executable is outside the discovered package root.";
        return false;
    }
    packageRoot = matches.front();
    return true;
}

std::wstring QuoteWindowsArgument(const std::wstring& value) {
    if (value.empty()) return L"\"\"";
    if (value.find_first_of(L" \t\"") == std::wstring::npos) return value;
    std::wstring quoted = L"\"";
    size_t backslashes = 0;
    for (wchar_t character : value) {
        if (character == L'\\') {
            ++backslashes;
        }
        else if (character == L'\"') {
            quoted.append(backslashes * 2 + 1, L'\\');
            quoted.push_back(L'\"');
            backslashes = 0;
        }
        else {
            quoted.append(backslashes, L'\\');
            backslashes = 0;
            quoted.push_back(character);
        }
    }
    quoted.append(backslashes * 2, L'\\');
    quoted.push_back(L'\"');
    return quoted;
}

std::wstring BuildAttachArguments(const ProxyBootstrapContext& context,
                                  const fs::path& packageRoot) {
    return L"--attach --root " + QuoteWindowsArgument(packageRoot.wstring()) +
        L" --game " + QuoteWindowsArgument(context.processImage.wstring()) +
        L" --pid " + std::to_wstring(context.processId) +
        L" --ready-event " + QuoteWindowsArgument(context.readyEventName) +
        L" --launch-id " + QuoteWindowsArgument(context.launchId);
}

ProxyBootstrapOutcome CoordinateProxyBootstrap(
    const ProxyBootstrapContext& context,
    const ProxyBootstrapHooks& hooks,
    Logger& log) {
    ProxyBootstrapOutcome outcome;
    if (!IsSupportedFf7ProcessName(context.processImage)) {
        outcome.state = ProxyBootstrapState::ForwardOnly;
        return outcome;
    }
    if (!hooks.validateHost || !hooks.applyPrivateRuntime ||
        !hooks.startBrokerAndWait ||
        (context.requireStockRuntimeReadiness &&
         !hooks.waitForStockRuntime)) {
        outcome.diagnostic = L"The proxy bootstrap hooks are incomplete.";
        return outcome;
    }
    if (!DiscoverPortableRoot(context.proxyModule, context.processImage,
            hooks.isCompleteRoot, outcome.packageRoot, outcome.diagnostic)) {
        return outcome;
    }

    const HostValidationResult host = hooks.validateHost(context.processImage);
    if (!host.supported) {
        outcome.state = ProxyBootstrapState::Unsupported;
        outcome.diagnostic = L"This FFVII executable failed the supported-host integrity check: " +
            host.diagnostic;
        return outcome;
    }
    if (context.requireStockRuntimeReadiness) {
        const StockRuntimeReadinessResult readiness =
            hooks.waitForStockRuntime(context.processImage, host, log);
        if (!readiness.ready) {
            outcome.diagnostic = readiness.diagnostic.empty()
                ? L"The stock FFVII runtime did not become ready."
                : readiness.diagnostic;
            return outcome;
        }
    }
    if (!hooks.applyPrivateRuntime(outcome.packageRoot, log)) {
        outcome.diagnostic = L"The private x86 .NET 9.0.8 runtime is unavailable.";
        return outcome;
    }

    const fs::path broker = outcome.packageRoot / L"Blind-Soldier" /
        L"Bootstrap" / L"x86" / L"Blind-Soldier-Bootstrap-x86.exe";
    const BrokerWaitResult result = hooks.startBrokerAndWait(
        broker, BuildAttachArguments(context, outcome.packageRoot),
        outcome.packageRoot, context.readyEventName, log);
    switch (result) {
        case BrokerWaitResult::Ready:
            outcome.state = ProxyBootstrapState::Ready;
            outcome.diagnostic.clear();
            break;
        case BrokerWaitResult::TimedOut:
            outcome.state = ProxyBootstrapState::TimedOut;
            outcome.diagnostic = L"The accessibility broker did not become ready within 30 seconds.";
            break;
        case BrokerWaitResult::BrokerExited:
            outcome.diagnostic = L"The accessibility broker exited before signaling readiness.";
            break;
        case BrokerWaitResult::TargetExited:
            outcome.diagnostic = L"FFVII exited before accessibility became ready.";
            break;
        case BrokerWaitResult::LaunchFailed:
            outcome.diagnostic = L"The accessibility broker could not be started.";
            break;
    }
    return outcome;
}

bool ApplyPrivateDotNetEnvironmentForProxy(
    const fs::path& packageRoot, Logger& log) {
    const fs::path runtimeRoot = packageRoot / L"Blind-Soldier" /
        L"Runtime" / L"dotnet" / L"x86";
    const fs::path hostFxr = runtimeRoot / L"host" / L"fxr" / L"9.0.8" /
        L"hostfxr.dll";
    const PeImageInfo image = InspectPeImage(hostFxr);
    if (!image.valid || image.machine != IMAGE_FILE_MACHINE_I386) {
        log.W(L"private x86 hostfxr validation failed: " + image.diagnostic);
        return false;
    }
    for (const wchar_t* name : {
            L"DOTNET_ROOT_X86", L"DOTNET_ROOT(x86)", L"DOTNET_ROOT"}) {
        if (!SetEnvironmentVariableW(name, runtimeRoot.c_str())) {
            log.Err(std::wstring(L"SetEnvironmentVariableW(") + name + L")",
                    GetLastError());
            return false;
        }
    }
    log.W(L"private x86 .NET root applied: " + runtimeRoot.wstring());
    return true;
}

BrokerWaitResult StartBrokerAndWaitForReady(
    const fs::path& broker,
    const std::wstring& arguments,
    const fs::path& workingDirectory,
    const std::wstring& readyEventName,
    Logger& log) {
    HANDLE readyEvent = CreateEventW(nullptr, TRUE, FALSE,
                                     readyEventName.c_str());
    if (!readyEvent) {
        log.Err(L"CreateEventW(proxy ready event)", GetLastError());
        return BrokerWaitResult::LaunchFailed;
    }

    std::wstring commandLine = QuoteWindowsArgument(broker.wstring()) + L" " +
        arguments;
    std::vector<wchar_t> mutableCommand(commandLine.begin(), commandLine.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const BOOL created = CreateProcessW(
        broker.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
        CREATE_NO_WINDOW, nullptr, workingDirectory.c_str(), &startup, &process);
    if (!created) {
        log.Err(L"CreateProcessW(x86 accessibility broker)", GetLastError());
        CloseHandle(readyEvent);
        return BrokerWaitResult::LaunchFailed;
    }
    CloseHandle(process.hThread);
    log.W(L"x86 broker started: " + broker.wstring());

    HANDLE currentProcess = nullptr;
    DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(),
                    GetCurrentProcess(), &currentProcess, SYNCHRONIZE,
                    FALSE, 0);
    std::array<HANDLE, 3> waits = {readyEvent, process.hProcess,
                                   currentProcess};
    const DWORD count = currentProcess ? 3 : 2;
    const DWORD wait = WaitForMultipleObjects(
        count, waits.data(), FALSE, kProxyReadyTimeoutMilliseconds);
    BrokerWaitResult result = BrokerWaitResult::TimedOut;
    if (wait == WAIT_OBJECT_0) {
        result = BrokerWaitResult::Ready;
        log.A("x86 broker signaled accessibility ready");
    }
    else if (wait == WAIT_OBJECT_0 + 1) {
        DWORD exitCode = 0;
        GetExitCodeProcess(process.hProcess, &exitCode);
        log.W(L"x86 broker exited before ready; exit=" +
              std::to_wstring(exitCode));
        result = BrokerWaitResult::BrokerExited;
    }
    else if (currentProcess && wait == WAIT_OBJECT_0 + 2) {
        result = BrokerWaitResult::TargetExited;
    }
    else if (wait == WAIT_FAILED) {
        log.Err(L"WaitForMultipleObjects(proxy readiness)", GetLastError());
        result = BrokerWaitResult::LaunchFailed;
    }
    if (currentProcess) CloseHandle(currentProcess);
    CloseHandle(process.hProcess);
    CloseHandle(readyEvent);
    return result;
}

#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
void InitializeWinmmProxy(HMODULE module) {
    InitializePortableBootstrap(module, true, L"WinMM");
}
#endif

void InitializePortableBootstrap(
    HMODULE module, bool loadWinmmForForwarding, const wchar_t* componentName,
    bool requireStockRuntimeReadiness,
    std::function<StockRuntimeReadinessResult(
        const fs::path&, const HostValidationResult&, Logger&)>
        waitForStockRuntime) {
    g_proxyModule = module;
#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
    g_loadWinmmForForwarding = loadWinmmForForwarding;
#else
    (void)loadWinmmForForwarding;
#endif
    g_requireStockRuntimeReadiness = requireStockRuntimeReadiness;
    g_waitForStockRuntime = std::move(waitForStockRuntime);
    g_bootstrapComponent = componentName && *componentName
        ? componentName : L"Bootstrap";
    g_workerFinished = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_workerFinished) {
        InterlockedExchange(&g_proxyState,
                            static_cast<LONG>(ProxyBootstrapState::Failed));
        return;
    }
    HANDLE worker = CreateThread(nullptr, 0, ProxyWorker, nullptr, 0, nullptr);
    if (!worker) {
        CompleteWorker(ProxyBootstrapState::Failed,
            L"Blind Soldier could not start accessibility.\n\nCause: The portable bootstrap worker could not start.\n\nAction: Restart the game. If this repeats, reinstall Blind Soldier.");
        return;
    }
    CloseHandle(worker);
}

void WaitForPortableBootstrap() {
    ProxyBootstrapState state = static_cast<ProxyBootstrapState>(
        InterlockedCompareExchange(&g_proxyState, 0, 0));
    if (state == ProxyBootstrapState::Pending) {
        const DWORD wait = g_workerFinished
            ? WaitForSingleObject(g_workerFinished, INFINITE)
            : WAIT_FAILED;
        if (wait != WAIT_OBJECT_0) {
            InterlockedCompareExchange(
                &g_proxyState,
                static_cast<LONG>(ProxyBootstrapState::Failed),
                static_cast<LONG>(ProxyBootstrapState::Pending));
            SetFailureMessage(
                L"Blind Soldier could not start accessibility.\n\nCause: Waiting for the portable bootstrap worker failed.\n\nAction: Restart the game and reinstall Blind Soldier if this repeats.");
        }
        state = static_cast<ProxyBootstrapState>(
            InterlockedCompareExchange(&g_proxyState, 0, 0));
    }
    if (state == ProxyBootstrapState::Ready ||
        state == ProxyBootstrapState::ForwardOnly) {
        return;
    }
    if (InterlockedCompareExchange(&g_failureShown, 1, 0) == 0) {
        const std::wstring failureMessage = FailureMessage();
        MessageBoxW(nullptr,
            failureMessage.empty()
                ? L"Blind Soldier could not start accessibility. Restart the game and reinstall Blind Soldier if this repeats."
                : failureMessage.c_str(),
            L"Blind Soldier", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
    }
    TerminateProcess(GetCurrentProcess(), 0xB51D0001u);
    ExitProcess(0xB51D0001u);
}

}  // namespace blind_soldier

#if !defined(BLIND_SOLDIER_NO_WINMM_FORWARDING)
extern "C" void __cdecl EnsureWinmmAndBootstrapReady() {
    blind_soldier::WaitForPortableBootstrap();
}
#endif
