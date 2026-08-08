#pragma once

#include "bootstrap_contract.h"
#include "reloaded_session.h"

namespace blind_soldier {

enum class InjectResult {
    Success,
    AllocFailed,
    WriteFailed,
    ResolveFailed,
    CreateThreadFailed,
    TimedOut,
    LoadLibraryFailed
};

LPVOID WaitForRemoteModuleBase(HANDLE process, DWORD processId,
                               const std::wstring& moduleName,
                               DWORD timeoutMilliseconds, Logger& log);

InjectResult InjectDll(HANDLE process, DWORD processId,
                       const std::wstring& dllPath,
                       DWORD timeoutMilliseconds, Logger& log);

BootstrapExitCode RunLaunch(const BootstrapRequest& request, Logger& log);
BootstrapExitCode RunAttach(const BootstrapRequest& request, Logger& log);

}  // namespace blind_soldier
