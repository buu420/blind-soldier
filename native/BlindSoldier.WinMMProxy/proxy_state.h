#pragma once

#include "../BlindSoldier.VersionProxy/app_loader_readiness.h"

#include <functional>

namespace blind_soldier {

constexpr size_t kWinmmExportCount = 193;
constexpr DWORD kProxyReadyTimeoutMilliseconds = 30000;
constexpr int kProxyRootSearchDepth = 4;

enum class ProxyBootstrapState : LONG {
    Pending,
    ForwardOnly,
    Ready,
    Unsupported,
    Failed,
    TimedOut
};

enum class BrokerWaitResult {
    Ready,
    BrokerExited,
    TimedOut,
    TargetExited,
    LaunchFailed
};

struct ProxyBootstrapContext {
    fs::path processImage;
    fs::path proxyModule;
    DWORD processId = 0;
    std::wstring launchId;
    std::wstring readyEventName;
    bool requireStockRuntimeReadiness = false;
};

struct ProxyBootstrapOutcome {
    ProxyBootstrapState state = ProxyBootstrapState::Failed;
    fs::path packageRoot;
    std::wstring diagnostic;
};

struct ProxyBootstrapHooks {
    std::function<bool(const fs::path&)> isCompleteRoot;
    std::function<HostValidationResult(const fs::path&)> validateHost;
    std::function<StockRuntimeReadinessResult(
        const fs::path&, const HostValidationResult&, Logger&)>
        waitForStockRuntime;
    std::function<bool(const fs::path&, Logger&)> applyPrivateRuntime;
    std::function<BrokerWaitResult(
        const fs::path&, const std::wstring&, const fs::path&,
        const std::wstring&, Logger&)> startBrokerAndWait;
};

bool IsSupportedFf7ProcessName(const fs::path& processImage);
bool IsCompletePortableRoot(const fs::path& candidate);
fs::path DeriveDiagnosticRoot(const fs::path& proxyModule);
std::wstring BuildReadyEventName(const std::wstring& launchId);
bool DiscoverPortableRoot(
    const fs::path& proxyModule,
    const fs::path& processImage,
    const std::function<bool(const fs::path&)>& isCompleteRoot,
    fs::path& packageRoot,
    std::wstring& diagnostic);
std::wstring QuoteWindowsArgument(const std::wstring& value);
std::wstring BuildAttachArguments(const ProxyBootstrapContext& context,
                                  const fs::path& packageRoot);
ProxyBootstrapOutcome CoordinateProxyBootstrap(
    const ProxyBootstrapContext& context,
    const ProxyBootstrapHooks& hooks,
    Logger& log);

bool ApplyPrivateDotNetEnvironmentForProxy(
    const fs::path& packageRoot, Logger& log);
BrokerWaitResult StartBrokerAndWaitForReady(
    const fs::path& broker,
    const std::wstring& arguments,
    const fs::path& workingDirectory,
    const std::wstring& readyEventName,
    Logger& log);

void InitializeWinmmProxy(HMODULE module);
void InitializePortableBootstrap(
    HMODULE module, bool loadWinmmForForwarding, const wchar_t* componentName,
    bool requireStockRuntimeReadiness = false,
    std::function<StockRuntimeReadinessResult(
        const fs::path&, const HostValidationResult&, Logger&)>
        waitForStockRuntime = {});
void WaitForPortableBootstrap();

}  // namespace blind_soldier

extern "C" FARPROC g_winmmExports[blind_soldier::kWinmmExportCount];
extern "C" void __cdecl EnsureWinmmAndBootstrapReady();
