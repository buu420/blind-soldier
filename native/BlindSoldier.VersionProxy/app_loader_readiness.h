#pragma once

#include "../BlindSoldier.Common/supported_hosts.h"

#include <windows.h>

#include <string>

namespace blind_soldier {

constexpr ULONGLONG kStockRuntimeReadinessTimeoutMilliseconds = 120000;

enum class AppLoaderGateState {
    Discovering,
    WaitingForCurrentLog,
    WaitingForSuccess,
    WaitingForProfileConsumption,
    ReadyDirect,
    ReadySeventhHeaven,
    Failed
};

struct AppLoaderObservation {
    SupportedHostKind hostKind = SupportedHostKind::None;
    bool stockLoaderSignaturePresent = false;
    bool stockWrapperModulesPresent = false;
    bool recognizedFfnxModulePresent = false;
    bool processAlive = true;
    ULONGLONG elapsedMilliseconds = 0;
    std::string appLoaderLog;
    FILETIME processCreation{};
    bool wrapperProfilePresent = false;
};

struct AppLoaderGateDecision {
    AppLoaderGateState state = AppLoaderGateState::Discovering;
    bool ready = false;
    bool seventhHeaven = false;
    std::wstring diagnostic;
};

struct StockRuntimeReadinessResult {
    bool ready = false;
    bool seventhHeaven = false;
    std::wstring diagnostic;
};

class AppLoaderReadinessTracker {
public:
    explicit AppLoaderReadinessTracker(
        ULONGLONG directDiscoveryMilliseconds = 3000,
        ULONGLONG timeoutMilliseconds =
        kStockRuntimeReadinessTimeoutMilliseconds);
    AppLoaderGateDecision Observe(const AppLoaderObservation& observation);

private:
    ULONGLONG directDiscoveryMilliseconds_;
    ULONGLONG timeoutMilliseconds_;
    bool seventhHeaven_ = false;
};

StockRuntimeReadinessResult WaitForStockRuntimeReadiness(
    const fs::path& processImage,
    const HostValidationResult& host,
    Logger& log,
    DWORD pollMilliseconds = 25,
    ULONGLONG timeoutMilliseconds = 120000);

}  // namespace blind_soldier
