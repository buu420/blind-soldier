#include "../BlindSoldier.WinMMProxy/proxy_state.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

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
    context.readyEventName = L"Local\\BlindSoldier-Ready-test";
    Touch(context.processImage);
    Touch(context.proxyModule);
    return context;
}

ProxyBootstrapHooks ReadyHooks(int& launchCount) {
    ProxyBootstrapHooks hooks;
    hooks.isCompleteRoot = IsCompletePortableRoot;
    hooks.validateHost = [](const fs::path&) { return SupportedHost(); };
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
              event == L"Local\\BlindSoldier-Ready-test",
              "ready event argument");
        return BrokerWaitResult::Ready;
    };
    return hooks;
}

void TestRootDiscovery() {
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

void TestArgumentsAndNames() {
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

}  // namespace

int wmain() {
    TestRootDiscovery();
    TestCoordinator();
    TestArgumentsAndNames();
    if (failures == 0) {
        std::cout << "Blind Soldier WinMM proxy behavior tests passed.\n";
    }
    return failures == 0 ? 0 : 1;
}
