#include "../BlindSoldier.WinMMProxy/proxy_state.h"
#include "../BlindSoldier.VersionProxy/app_loader_readiness.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <utility>
#include <vector>

namespace fs = std::filesystem;
using namespace blind_soldier;

namespace {

int failures = 0;

void Check(bool condition, const char* label) {
    if (!condition) {
        std::cerr << "FAIL: " << label << "\n";
        ++failures;
    }
}

fs::path NewTempRoot(const wchar_t* label) {
    wchar_t temp[MAX_PATH]{};
    GetTempPathW(ARRAYSIZE(temp), temp);
    return fs::path(temp) /
        (std::wstring(L"blind-soldier-winmm-") + label + L"-" +
         std::to_wstring(GetCurrentProcessId()) + L"-" +
         std::to_wstring(GetTickCount64()));
}

void Touch(const fs::path& path) {
    fs::create_directories(path.parent_path());
    std::ofstream stream(path, std::ios::binary);
    stream << "fixture";
}

void MakeComplete(const fs::path& root) {
    for (const fs::path& relative : {
            fs::path(L"Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe"),
            fs::path(L"Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll"),
            fs::path(L"Reloaded-II/portable.txt"),
            fs::path(L"Reloaded-II/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll"),
            fs::path(L"Reloaded-II/Loader/X86/Reloaded.Mod.Loader.dll")}) {
        Touch(root / relative);
    }
}

struct TempTree {
    explicit TempTree(const wchar_t* label) : root(NewTempRoot(label)) {
        fs::create_directories(root);
    }
    ~TempTree() {
        std::error_code error;
        fs::remove_all(root, error);
    }
    fs::path root;
};

HostValidationResult SupportedHost() {
    HostValidationResult result;
    result.supported = true;
    result.kind = SupportedHostKind::LegacyStockX86;
    result.diagnostic = L"fixture accepted";
    return result;
}

ProxyBootstrapContext MakeDirectContext(const fs::path& root,
                                        const wchar_t* processName) {
    ProxyBootstrapContext context;
    context.processImage = root / processName;
    context.proxyModule = root /
        (std::wstring(processName) + L".local") / L"winmm.dll";
    context.processId = 4242;
    context.launchId = L"12345678-1234-1234-1234-1234567890AB";
    context.readyEventName = BuildReadyEventName(context.launchId);
    Touch(context.processImage);
    Touch(context.proxyModule);
    return context;
}

ProxyBootstrapContext MakeSiblingContext(const fs::path& root,
                                         const wchar_t* processName) {
    ProxyBootstrapContext context;
    context.processImage = root / processName;
    context.proxyModule = root / L"version.dll";
    context.processId = 4242;
    context.launchId = L"12345678-1234-1234-1234-1234567890AB";
    context.readyEventName = BuildReadyEventName(context.launchId);
    context.requireStockRuntimeReadiness = true;
    Touch(context.processImage);
    Touch(context.proxyModule);
    return context;
}

ProxyBootstrapHooks ReadyHooks(int& launchCount) {
    ProxyBootstrapHooks hooks;
    hooks.isCompleteRoot = IsCompletePortableRoot;
    hooks.validateHost = [](const fs::path&) { return SupportedHost(); };
    hooks.waitForStockRuntime = [](
        const fs::path&, const HostValidationResult&, Logger&) {
        StockRuntimeReadinessResult result;
        result.ready = true;
        return result;
    };
    hooks.applyPrivateRuntime = [](const fs::path&, Logger&) { return true; };
    hooks.startBrokerAndWait = [&launchCount](
        const fs::path& broker, const std::wstring& arguments,
        const fs::path&, const std::wstring& event, Logger&) {
        ++launchCount;
        Check(broker.filename() == L"Blind-Soldier-Bootstrap-x86.exe",
              "exact x86 broker path");
        Check(arguments.find(L"--attach") != std::wstring::npos,
              "attach argument");
        Check(arguments.find(L"--pid 4242") != std::wstring::npos,
              "pid argument");
        Check(arguments.find(L"--ready-event") != std::wstring::npos &&
              event ==
                  L"Local\\BlindSoldier.Ready.12345678-1234-1234-1234-1234567890AB",
              "ready event argument");
        return BrokerWaitResult::Ready;
    };
    return hooks;
}

void TestRootDiscovery() {
    {
        TempTree tree(L"version-direct");
        MakeComplete(tree.root);
        const auto context = MakeSiblingContext(tree.root, L"ff7_en.exe");
        fs::path root;
        std::wstring diagnostic;
        const bool discovered = DiscoverPortableRoot(
            context.proxyModule, context.processImage,
            IsCompletePortableRoot, root, diagnostic);
        Check(discovered, "sibling version proxy root discovered");
        if (discovered) {
            Check(fs::equivalent(root, tree.root),
                  "sibling version proxy root exact");
        }
        Check(fs::equivalent(DeriveDiagnosticRoot(context.proxyModule),
                             tree.root),
              "sibling version diagnostic root");
    }
    {
        TempTree tree(L"version-nested");
        MakeComplete(tree.root);
        const fs::path working = tree.root / L"ff7" / L"workingdir";
        const auto context = MakeSiblingContext(working, L"ff7.exe");
        fs::path root;
        std::wstring diagnostic;
        const bool discovered = DiscoverPortableRoot(
            context.proxyModule, context.processImage,
            IsCompletePortableRoot, root, diagnostic);
        Check(discovered, "nested sibling version proxy root discovered");
        if (discovered) {
            Check(fs::equivalent(root, tree.root),
                  "nested sibling version proxy root exact");
        }
        Check(fs::equivalent(DeriveDiagnosticRoot(context.proxyModule),
                             tree.root),
              "nested sibling version diagnostic root");
    }
    {
        TempTree tree(L"direct");
        MakeComplete(tree.root);
        const auto context = MakeDirectContext(tree.root, L"ff7_en.exe");
        fs::path root;
        std::wstring diagnostic;
        Check(DiscoverPortableRoot(context.proxyModule, context.processImage,
                  IsCompletePortableRoot, root, diagnostic),
              "direct root discovered");
        Check(fs::equivalent(root, tree.root), "direct root exact");
        Check(fs::equivalent(DeriveDiagnosticRoot(context.proxyModule),
                             tree.root),
              "direct diagnostic root");
    }
    {
        TempTree tree(L"nested");
        MakeComplete(tree.root);
        const fs::path working = tree.root / L"ff7" / L"workingdir";
        auto context = MakeDirectContext(working, L"ff7.exe");
        fs::path root;
        std::wstring diagnostic;
        Check(DiscoverPortableRoot(context.proxyModule, context.processImage,
                  IsCompletePortableRoot, root, diagnostic),
              "nested 7th Heaven root discovered");
        Check(fs::equivalent(root, tree.root), "nested root exact");
        Check(fs::equivalent(DeriveDiagnosticRoot(context.proxyModule),
                             tree.root),
              "nested diagnostic root");
    }
    {
        TempTree tree(L"incomplete");
        const auto context = MakeDirectContext(tree.root, L"ff7_en.exe");
        fs::path root;
        std::wstring diagnostic;
        Check(!DiscoverPortableRoot(context.proxyModule, context.processImage,
                  IsCompletePortableRoot, root, diagnostic),
              "incomplete root rejected");
    }
    {
        TempTree tree(L"ambiguous");
        const fs::path child = tree.root / L"game";
        MakeComplete(tree.root);
        MakeComplete(child);
        const auto context = MakeDirectContext(child, L"ff7_en.exe");
        fs::path root;
        std::wstring diagnostic;
        Check(!DiscoverPortableRoot(context.proxyModule, context.processImage,
                  IsCompletePortableRoot, root, diagnostic) &&
              diagnostic.find(L"More than one") != std::wstring::npos,
              "ambiguous complete roots rejected");
    }
    {
        TempTree tree(L"bounded");
        MakeComplete(tree.root);
        const fs::path deep = tree.root / L"a" / L"b" / L"c" / L"d" /
            L"e";
        const auto context = MakeDirectContext(deep, L"ff7_en.exe");
        fs::path root;
        std::wstring diagnostic;
        Check(!DiscoverPortableRoot(context.proxyModule, context.processImage,
                  IsCompletePortableRoot, root, diagnostic),
              "root search is bounded to four parents");
    }
}

void TestCoordinator() {
    TempTree tree(L"coordinator");
    MakeComplete(tree.root);
    Logger log;
    log.Open(tree.root / L"logs", L"test.log");
    auto context = MakeDirectContext(tree.root, L"ff7_en.exe");
    int launches = 0;
    auto hooks = ReadyHooks(launches);
    auto outcome = CoordinateProxyBootstrap(context, hooks, log);
    Check(outcome.state == ProxyBootstrapState::Ready,
          "supported host reaches ready");
    Check(launches == 1, "one broker launch per coordinator run");

    context.processImage = tree.root / L"unrelated.exe";
    Touch(context.processImage);
    launches = 0;
    outcome = CoordinateProxyBootstrap(context, hooks, log);
    Check(outcome.state == ProxyBootstrapState::ForwardOnly && launches == 0,
          "unrelated process forwards without broker");

    context = MakeDirectContext(tree.root, L"ff7.exe");
    hooks = ReadyHooks(launches);
    hooks.validateHost = [](const fs::path&) {
        HostValidationResult result;
        result.diagnostic = L"synthetic corruption";
        return result;
    };
    launches = 0;
    outcome = CoordinateProxyBootstrap(context, hooks, log);
    Check(outcome.state == ProxyBootstrapState::Unsupported && launches == 0,
          "supported FFVII name with invalid fingerprint fails closed");

    for (const auto& test : {
            std::pair{BrokerWaitResult::BrokerExited,
                      ProxyBootstrapState::Failed},
            std::pair{BrokerWaitResult::TimedOut,
                      ProxyBootstrapState::TimedOut},
            std::pair{BrokerWaitResult::TargetExited,
                      ProxyBootstrapState::Failed},
            std::pair{BrokerWaitResult::LaunchFailed,
                      ProxyBootstrapState::Failed}}) {
        launches = 0;
        hooks = ReadyHooks(launches);
        hooks.startBrokerAndWait = [&launches, test](
            const fs::path&, const std::wstring&, const fs::path&,
            const std::wstring&, Logger&) {
            ++launches;
            return test.first;
        };
        outcome = CoordinateProxyBootstrap(context, hooks, log);
        Check(outcome.state == test.second && launches == 1,
              "broker wait failure state");
    }
    log.Close();
}


void TestVersionReadinessCoordinatorBoundary() {
    TempTree tree(L"version-readiness-coordinator");
    MakeComplete(tree.root);
    Logger log;
    log.Open(tree.root / L"logs", L"test.log");

    {
        auto context = MakeSiblingContext(tree.root, L"ff7_en.exe");
        int launches = 0;
        int runtimeApplications = 0;
        std::vector<std::string> calls;
        auto hooks = ReadyHooks(launches);
        hooks.validateHost = [&calls](const fs::path&) {
            calls.push_back("validate-host");
            return SupportedHost();
        };
        hooks.waitForStockRuntime = [&calls](const fs::path&,
            const HostValidationResult&, Logger&) {
            calls.push_back("wait-stock-runtime");
            StockRuntimeReadinessResult result;
            result.ready = true;
            result.seventhHeaven = true;
            return result;
        };
        hooks.applyPrivateRuntime = [&calls, &runtimeApplications](
            const fs::path&, Logger&) {
            calls.push_back("apply-private-runtime");
            ++runtimeApplications;
            return true;
        };
        hooks.startBrokerAndWait = [&calls, &launches](const fs::path&,
            const std::wstring&, const fs::path&, const std::wstring&, Logger&) {
            calls.push_back("start-broker");
            ++launches;
            return BrokerWaitResult::Ready;
        };
        const auto outcome = CoordinateProxyBootstrap(context, hooks, log);
        Check(outcome.state == ProxyBootstrapState::Ready,
              "Version readiness success reaches ready");
        Check(runtimeApplications == 1 && launches == 1,
              "Version readiness success applies runtime and starts broker once");
        Check(calls == std::vector<std::string>{"validate-host",
                  "wait-stock-runtime", "apply-private-runtime", "start-broker"},
              "Version readiness runs after host validation and before runtime or broker");
    }

    {
        auto context = MakeSiblingContext(tree.root, L"ff7.exe");
        int launches = 0;
        int runtimeApplications = 0;
        auto hooks = ReadyHooks(launches);
        hooks.waitForStockRuntime = [root = tree.root](const fs::path&,
            const HostValidationResult&, Logger&) {
            StockRuntimeReadinessResult result;
            result.diagnostic = L"Timed out waiting for current AppLoader launch at " +
                (root / L"AppLoader.log").wstring();
            return result;
        };
        hooks.applyPrivateRuntime = [&runtimeApplications](const fs::path&, Logger&) {
            ++runtimeApplications;
            return true;
        };
        const auto outcome = CoordinateProxyBootstrap(context, hooks, log);
        const std::wstring expected =
            L"Timed out waiting for current AppLoader launch at " +
            (tree.root / L"AppLoader.log").wstring();
        Check(outcome.state == ProxyBootstrapState::Failed,
              "Version readiness timeout fails bootstrap");
        Check(outcome.diagnostic == expected,
              "Version readiness timeout preserves the gate diagnostic");
        Check(runtimeApplications == 0 && launches == 0,
              "Version readiness failure blocks runtime and broker side effects");
    }

    {
        auto context = MakeDirectContext(tree.root, L"ff7_en.exe");
        int launches = 0;
        int readinessCalls = 0;
        auto hooks = ReadyHooks(launches);
        hooks.waitForStockRuntime = [&readinessCalls](const fs::path&,
            const HostValidationResult&, Logger&) {
            ++readinessCalls;
            StockRuntimeReadinessResult result;
            result.diagnostic = L"WinMM must not use the stock-runtime gate.";
            return result;
        };
        const auto outcome = CoordinateProxyBootstrap(context, hooks, log);
        Check(outcome.state == ProxyBootstrapState::Ready && launches == 1,
              "historical WinMM context remains ungated");
        Check(readinessCalls == 0,
              "historical WinMM context does not call stock-runtime readiness");
    }
    log.Close();
}

void TestBootstrapWaitBudgets() {
    Check(PortableBootstrapWaitMilliseconds(false) ==
              kProxyReadyTimeoutMilliseconds,
          "direct WinMM bootstrap retains the 30 second outer bound");
    Check(PortableBootstrapWaitMilliseconds(true) == INFINITE,
          "stock 7th Heaven Version bootstrap uses phase-specific deadlines");
}
void TestArgumentsAndNames() {
    Check(BuildReadyEventName(L"01234567") ==
              L"Local\\BlindSoldier.Ready.01234567",
          "proxy and broker share the canonical ready-event contract");
    Check(IsSupportedFf7ProcessName(L"C:/Game/FF7_EN.EXE"),
          "ff7_en name accepted case-insensitively");
    Check(IsSupportedFf7ProcessName(L"C:/Game/ff7.exe"),
          "ff7 name accepted");
    Check(!IsSupportedFf7ProcessName(L"C:/Game/FFVII.exe"),
          "x64 host not accepted by x86 proxy");
    Check(QuoteWindowsArgument(L"C:\\Game Path\\") ==
              L"\"C:\\Game Path\\\\\"",
          "Windows trailing slash quoting");
}

FILETIME FileTimeBeforeNow(ULONGLONG milliseconds) {
    FILETIME now{};
    GetSystemTimeAsFileTime(&now);
    ULARGE_INTEGER value{};
    value.LowPart = now.dwLowDateTime;
    value.HighPart = now.dwHighDateTime;
    value.QuadPart -= milliseconds * 10000ULL;
    FILETIME result{};
    result.dwLowDateTime = value.LowPart;
    result.dwHighDateTime = value.HighPart;
    return result;
}

std::string CurrentTimestamp() {
    SYSTEMTIME local{};
    GetLocalTime(&local);
    char timestamp[24]{};
    sprintf_s(timestamp, "%04u-%02u-%02u %02u:%02u:%02u.%03u",
              local.wYear, local.wMonth, local.wDay, local.wHour,
              local.wMinute, local.wSecond, local.wMilliseconds);
    return timestamp;
}

std::string CurrentLine(const char* message) {
    return CurrentTimestamp() + " INFO  AppLoader " + message;
}

std::string CurrentLines(std::initializer_list<const char*> messages) {
    std::string result;
    for (const char* message : messages) {
        if (!result.empty()) result += '\n';
        result += CurrentLine(message);
    }
    return result;
}

AppLoaderObservation Observation(SupportedHostKind hostKind,
                                 bool stockLoaderSignaturePresent,
                                 ULONGLONG elapsedMilliseconds,
                                 std::string appLoaderLog,
                                 bool wrapperProfilePresent,
                                 bool recognizedFfnxModulePresent = false,
                                 bool processAlive = true) {
    AppLoaderObservation observation;
    observation.hostKind = hostKind;
    observation.stockLoaderSignaturePresent = stockLoaderSignaturePresent;
    observation.recognizedFfnxModulePresent = recognizedFfnxModulePresent;
    observation.processAlive = processAlive;
    observation.elapsedMilliseconds = elapsedMilliseconds;
    observation.appLoaderLog = std::move(appLoaderLog);
    observation.processCreation = FileTimeBeforeNow(60000);
    observation.wrapperProfilePresent = wrapperProfilePresent;
    return observation;
}

void CheckState(const AppLoaderGateDecision& decision,
                AppLoaderGateState expected, const char* label) {
    Check(decision.state == expected, label);
}

void TestAppLoaderReadiness() {
    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::LegacyStockX86,
                                            false, 2999, "", false)),
                   AppLoaderGateState::Discovering,
                   "exact stock host remains discovering before 3000 ms");
        const auto decision = gate.Observe(Observation(
            SupportedHostKind::LegacyStockX86, false, 3000, "", false));
        CheckState(decision, AppLoaderGateState::ReadyDirect,
                   "exact stock host becomes ready direct at 3000 ms");
        Check(decision.ready && !decision.seventhHeaven,
              "direct readiness flags are correct");
    }

    for (const auto hostKind : {SupportedHostKind::None,
                                SupportedHostKind::Steam2026X64}) {
        AppLoaderReadinessTracker gate(3000, 120000);
        const auto decision = gate.Observe(Observation(hostKind, false, 3000,
                                                       "", false));
        CheckState(decision, AppLoaderGateState::Failed,
                   "unsupported host never becomes direct-ready");
        Check(!decision.ready && !decision.diagnostic.empty(),
              "unsupported host fails closed with a diagnostic");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            false, 2999, "", false, true)),
            AppLoaderGateState::Discovering,
            "converted FFNx-only no-mod host remains in discovery");
        const auto ready = gate.Observe(Observation(
            SupportedHostKind::SevenHeavenX86, false, 3000, "", false, true));
        CheckState(ready, AppLoaderGateState::ReadyDirect,
            "converted FFNx-only no-mod host becomes ready direct");
        Check(ready.ready && !ready.seventhHeaven,
            "FFNx presence alone does not claim an AppLoader run");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        const auto ready = gate.Observe(Observation(
            SupportedHostKind::SevenHeavenX86, false, 3000, "", false));
        CheckState(ready, AppLoaderGateState::ReadyDirect,
            "converted no-mod host becomes ready direct without FFNx evidence");
    }

    for (const int evidence : {0, 1, 2}) {
        AppLoaderReadinessTracker gate(3000, 120000);
        const bool stockLoader = evidence == 0;
        const bool wrapperProfile = evidence == 1;
        const std::string currentLog = evidence == 2
            ? CurrentLine("init log") : "";
        const auto first = gate.Observe(Observation(
            SupportedHostKind::SevenHeavenX86, stockLoader, 10,
            currentLog, wrapperProfile));
        CheckState(first, evidence == 2
                ? AppLoaderGateState::WaitingForSuccess
                : AppLoaderGateState::WaitingForCurrentLog,
            "real AppLoader-run evidence enters the AppLoader branch");
        const auto sticky = gate.Observe(Observation(
            SupportedHostKind::SevenHeavenX86, false, 11, "", false));
        CheckState(sticky, AppLoaderGateState::WaitingForCurrentLog,
            "real AppLoader-run evidence stays sticky");
        Check(sticky.seventhHeaven,
            "sticky branch is identified as an AppLoader run");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLine("init log"), false)),
            AppLoaderGateState::WaitingForSuccess,
            "current AppLoader init waits for success");
        const auto ready = gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 20, CurrentLines({"init log", "started successfully"}), false));
        CheckState(ready, AppLoaderGateState::ReadySeventhHeaven,
                   "current AppLoader init and success are ready");
        Check(ready.ready && ready.seventhHeaven,
              "7th Heaven readiness flags are correct");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLine("started successfully"), false)),
            AppLoaderGateState::WaitingForCurrentLog,
            "success without init is rejected");
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10,
            "2001-01-01 00:00:00.000 INFO  AppLoader init log\n"
            "2001-01-01 00:00:01.000 INFO  AppLoader started successfully", false)),
            AppLoaderGateState::WaitingForCurrentLog,
            "stale prior-process records are rejected");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10,
            CurrentLines({"init log", "started successfully", "init log"}), false)),
            AppLoaderGateState::WaitingForSuccess,
            "only the last current init section is used");
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLines({"started successfully", "init log"}), false)),
            AppLoaderGateState::WaitingForSuccess,
            "success before last init is rejected");
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLines({"init log", "started successful"}), false)),
            AppLoaderGateState::WaitingForSuccess,
            "truncated success line waits");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLines({"init log", "started successfully"}), true)),
            AppLoaderGateState::WaitingForProfileConsumption,
            "present wrapper profile waits for consumption");
        const auto timedOut = gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 120000, CurrentLines({"init log", "started successfully"}), true));
        CheckState(timedOut, AppLoaderGateState::Failed,
                   "wrapper profile consumption fails at timeout");
        Check(!timedOut.diagnostic.empty(), "profile timeout has diagnostic");
    }

    {
        AppLoaderReadinessTracker gate(3000, 120000);
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 10, CurrentLines({"init log", "started successfully"}), true)),
            AppLoaderGateState::WaitingForProfileConsumption,
            "wrapper profile is initially pending");
        CheckState(gate.Observe(Observation(SupportedHostKind::SevenHeavenX86,
            true, 11, CurrentLines({"init log", "started successfully"}), false)),
            AppLoaderGateState::ReadySeventhHeaven,
            "profile removal completes readiness");
    }

    for (const int failure : {0, 1}) {
        AppLoaderReadinessTracker gate(3000, 120000);
        const auto decision = gate.Observe(Observation(
            SupportedHostKind::SevenHeavenX86, true,
            failure == 0 ? 10 : 120000, "", false, false, failure != 0));
        CheckState(decision, AppLoaderGateState::Failed,
                   "process exit and timeout fail the gate");
        Check(!decision.diagnostic.empty(),
              "process exit and timeout provide diagnostics");
    }


    {
        TempTree tree(L"absolute-readiness-diagnostic");
        Logger log;
        log.Open(tree.root / L"logs", L"test.log");
        const auto failed = WaitForStockRuntimeReadiness(
            fs::path(L"?:\\invalid\\ff7_en.exe"), SupportedHost(), log, 0, 1);
        const std::wstring marker = L"AppLoader.log: ";
        const size_t markerAt = failed.diagnostic.find(marker);
        Check(!failed.ready && markerAt != std::wstring::npos,
              "readiness failure names AppLoader log");
        if (markerAt != std::wstring::npos) {
            const fs::path reported(
                failed.diagnostic.substr(markerAt + marker.size()));
            Check(reported.is_absolute() &&
                      reported.filename() == L"AppLoader.log",
                  "readiness failure reports an absolute AppLoader log path");
        }
        log.Close();
    }
}

}  // namespace

int wmain() {
    TestRootDiscovery();
    TestCoordinator();
    TestVersionReadinessCoordinatorBoundary();
    TestBootstrapWaitBudgets();
    TestArgumentsAndNames();
    TestAppLoaderReadiness();
    if (failures == 0) {
        std::cout << "Blind Soldier WinMM proxy behavior tests passed.\n";
    }
    return failures == 0 ? 0 : 1;
}
