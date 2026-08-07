#pragma once

#include "../BlindSoldier.Common/supported_hosts.h"

#include <windows.h>

#include <string>

namespace blind_soldier {

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

class AppLoaderReadinessTracker {
public:
    explicit AppLoaderReadinessTracker(
        ULONGLONG directDiscoveryMilliseconds = 3000,
        ULONGLONG timeoutMilliseconds = 120000);
    AppLoaderGateDecision Observe(const AppLoaderObservation& observation);

private:
    ULONGLONG directDiscoveryMilliseconds_;
    ULONGLONG timeoutMilliseconds_;
    bool seventhHeaven_ = false;
};

}  // namespace blind_soldier
