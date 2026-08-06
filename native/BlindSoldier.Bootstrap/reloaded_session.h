#pragma once

#include "bootstrap_contract.h"
#include "../BlindSoldier.Common/supported_hosts.h"

namespace blind_soldier {

struct ValidatedPayload {
    fs::path packageRoot;
    fs::path reloadedRoot;
    fs::path privateRuntimeRoot;
    fs::path bootstrapper;
    fs::path loader;
    fs::path accessibilityConfig;
    fs::path accessibilityAssembly;
    fs::path hooksConfig;
    fs::path hooksAssembly;
    fs::path prism;
};

class ReloadedPointerLease {
public:
    ReloadedPointerLease(
        const fs::path& reloadedRoot,
        Logger& log,
        DWORD waitMilliseconds = 20000,
        const fs::path& pointerOverride = {});
    ~ReloadedPointerLease();

    ReloadedPointerLease(const ReloadedPointerLease&) = delete;
    ReloadedPointerLease& operator=(const ReloadedPointerLease&) = delete;

    bool Ready() const { return ready_; }
    const std::wstring& Diagnostic() const { return diagnostic_; }

private:
    void RestoreAfterFailedWrite();
    void Fail(const std::wstring& diagnostic);

    fs::path pointer_;
    fs::path backup_;
    std::string portableContent_;
    bool hadOriginal_ = false;
    bool ready_ = false;
    HANDLE mutex_ = nullptr;
    bool ownsMutex_ = false;
    Logger* log_ = nullptr;
    std::wstring diagnostic_;
};

bool ValidatePortablePayload(
    const BootstrapRequest& request,
    ExpectedHostArchitecture architecture,
    ValidatedPayload& payload,
    Logger& log,
    bool writeAppConfig = true);

bool IsCanonicalPathWithinRoot(
    const fs::path& root,
    const fs::path& candidate,
    fs::path& canonicalCandidate,
    std::wstring& diagnostic);

}  // namespace blind_soldier
