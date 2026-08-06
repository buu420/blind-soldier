#pragma once

#include "../BlindSoldier.Common/common.h"

namespace blind_soldier {

enum class BootstrapMode { Launch, Attach };

enum class BootstrapExitCode : int {
    Success = 0,
    InvalidArguments = 10,
    UnsupportedHost = 11,
    MissingPayload = 12,
    PointerLeaseUnavailable = 13,
    TargetUnavailable = 14,
    ArchitectureMismatch = 15,
    AppConfigFailed = 16,
    InjectionFailed = 17,
    ResumeFailed = 18,
    RuntimeUnavailable = 19,
    ReadySignalFailed = 20
};

struct BootstrapRequest {
    BootstrapMode mode = BootstrapMode::Launch;
    fs::path packageRoot;
    fs::path gameExecutable;
    DWORD processId = 0;
    std::wstring gameArguments;
    std::wstring readyEventName;
    std::wstring launchId;
};

bool TryParseBootstrapRequest(
    const std::vector<std::wstring>& arguments,
    BootstrapRequest& request,
    std::wstring& error);

BootstrapExitCode RunBootstrap(const BootstrapRequest& request, Logger& log);

}  // namespace blind_soldier
