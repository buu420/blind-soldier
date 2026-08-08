#include "process_bootstrap.h"

#include "../BlindSoldier.Common/pe_image.h"
#include "../BlindSoldier.Common/supported_hosts.h"

#include <tlhelp32.h>

#include <memory>
#include <vector>

namespace blind_soldier {
namespace {

class EnvironmentSnapshot {
public:
    explicit EnvironmentSnapshot(const wchar_t* name) : name_(name) {
        DWORD required = GetEnvironmentVariableW(name_.c_str(), nullptr, 0);
        if (required > 0) {
            std::vector<wchar_t> buffer(required);
            DWORD copied = GetEnvironmentVariableW(
                name_.c_str(), buffer.data(), required);
            if (copied > 0 && copied < required) {
                hadValue_ = true;
                prior_.assign(buffer.data(), copied);
            }
        }
    }

    void Restore() const {
        SetEnvironmentVariableW(name_.c_str(),
            hadValue_ ? prior_.c_str() : nullptr);
    }

private:
    std::wstring name_;
    std::wstring prior_;
    bool hadValue_ = false;
};

class ScopedPrivateDotNetEnvironment {
public:
    ScopedPrivateDotNetEnvironment(ExpectedHostArchitecture architecture,
                                   const fs::path& runtimeRoot, Logger& log)
        : root_(L"DOTNET_ROOT"),
          architecture_(architecture == ExpectedHostArchitecture::X86
              ? L"DOTNET_ROOT_X86" : L"DOTNET_ROOT_X64"),
          legacyX86_(architecture == ExpectedHostArchitecture::X86
              ? std::make_unique<EnvironmentSnapshot>(L"DOTNET_ROOT(x86)")
              : nullptr) {
        applied_ = ApplyPrivateDotNetEnvironment(architecture, runtimeRoot, log);
    }

    ~ScopedPrivateDotNetEnvironment() {
        if (legacyX86_) legacyX86_->Restore();
        architecture_.Restore();
        root_.Restore();
    }

    bool Applied() const { return applied_; }

private:
    EnvironmentSnapshot root_;
    EnvironmentSnapshot architecture_;
    std::unique_ptr<EnvironmentSnapshot> legacyX86_;
    bool applied_ = false;
};

LPTHREAD_START_ROUTINE ResolveRemoteLoadLibraryW(HANDLE process,
                                                  DWORD processId,
                                                  Logger& log) {
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC loadLibrary = kernel32
        ? GetProcAddress(kernel32, "LoadLibraryW") : nullptr;
    if (!loadLibrary) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetProcAddress", GetLastError());
        return nullptr;
    }
    HMODULE localOwner = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(loadLibrary), &localOwner)) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleHandleExW",
                GetLastError());
        return nullptr;
    }
    std::vector<wchar_t> path(32768);
    DWORD length = GetModuleFileNameW(
        localOwner, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        log.Err(L"ResolveRemoteLoadLibraryW: GetModuleFileNameW",
                GetLastError());
        return nullptr;
    }
    fs::path owner(path.data(), path.data() + length);
    LPVOID remoteBase = WaitForRemoteModuleBase(
        process, processId, owner.filename().wstring(), 5000, log);
    if (!remoteBase) return nullptr;
    uintptr_t relative = reinterpret_cast<uintptr_t>(loadLibrary) -
                         reinterpret_cast<uintptr_t>(localOwner);
    return reinterpret_cast<LPTHREAD_START_ROUTINE>(
        reinterpret_cast<uintptr_t>(remoteBase) + relative);
}

bool CanonicalPathsEqual(const fs::path& left, const fs::path& right) {
    std::error_code leftError;
    std::error_code rightError;
    fs::path canonicalLeft = fs::canonical(left, leftError);
    fs::path canonicalRight = fs::canonical(right, rightError);
    return !leftError && !rightError &&
           _wcsicmp(canonicalLeft.c_str(), canonicalRight.c_str()) == 0;
}

bool GetProcessPath(HANDLE process, fs::path& result, Logger& log) {
    std::vector<wchar_t> buffer(32768);
    DWORD length = static_cast<DWORD>(buffer.size());
    if (!QueryFullProcessImageNameW(process, 0, buffer.data(), &length)) {
        log.Err(L"QueryFullProcessImageNameW", GetLastError());
        return false;
    }
    result.assign(buffer.data(), buffer.data() + length);
    return true;
}

bool GetProcessMachine(HANDLE process, uint16_t& result, Logger& log) {
    using IsWow64Process2Function = BOOL (WINAPI*)(HANDLE, USHORT*, USHORT*);
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    auto function = reinterpret_cast<IsWow64Process2Function>(
        kernel32 ? GetProcAddress(kernel32, "IsWow64Process2") : nullptr);
    if (!function) {
        BOOL wow64 = FALSE;
        if (!IsWow64Process(process, &wow64)) {
            log.Err(L"IsWow64Process", GetLastError());
            return false;
        }
#ifdef _WIN64
        result = wow64 ? IMAGE_FILE_MACHINE_I386 : IMAGE_FILE_MACHINE_AMD64;
#else
        result = IMAGE_FILE_MACHINE_I386;
#endif
        return true;
    }
    USHORT processMachine = IMAGE_FILE_MACHINE_UNKNOWN;
    USHORT nativeMachine = IMAGE_FILE_MACHINE_UNKNOWN;
    if (!function(process, &processMachine, &nativeMachine)) {
        log.Err(L"IsWow64Process2", GetLastError());
        return false;
    }
    result = processMachine == IMAGE_FILE_MACHINE_UNKNOWN
        ? nativeMachine : processMachine;
    return true;
}

BootstrapExitCode WaitForTarget(HANDLE process, Logger& log) {
    unsigned int heartbeat = 0;
    for (;;) {
        DWORD wait = WaitForSingleObject(process, 30000);
        if (wait == WAIT_OBJECT_0) return BootstrapExitCode::Success;
        if (wait == WAIT_TIMEOUT) {
            log.W(L"Target is still running; heartbeat=" +
                  std::to_wstring(++heartbeat));
            continue;
        }
        log.Err(L"WaitForSingleObject(target)", GetLastError());
        return BootstrapExitCode::TargetUnavailable;
    }
}

bool ValidateRequestPaths(const BootstrapRequest& request, Logger& log) {
    fs::path canonical;
    std::wstring diagnostic;
    if (!IsCanonicalPathWithinRoot(request.packageRoot,
            request.gameExecutable, canonical, diagnostic)) {
        log.W(L"Request path validation failed: " + diagnostic);
        return false;
    }
    return true;
}

BootstrapExitCode ValidateHostFile(
    const fs::path& game, ExpectedHostArchitecture architecture,
    Logger& log) {
    PeImageInfo image = InspectPeImage(game);
    if (!image.valid) {
        log.W(L"Host PE validation failed: " + image.diagnostic);
        return BootstrapExitCode::UnsupportedHost;
    }
    uint16_t expected = architecture == ExpectedHostArchitecture::X86
        ? IMAGE_FILE_MACHINE_I386 : IMAGE_FILE_MACHINE_AMD64;
    if (image.machine != expected) {
        log.W(L"Host architecture does not match this bootstrap executable.");
        return BootstrapExitCode::ArchitectureMismatch;
    }
    HostValidationResult validation = ValidateSupportedHost(game, architecture);
    log.W(L"Host validation: " + validation.diagnostic);
    return validation.supported ? BootstrapExitCode::Success
                                : BootstrapExitCode::UnsupportedHost;
}

}  // namespace

LPVOID WaitForRemoteModuleBase(HANDLE process, DWORD processId,
                               const std::wstring& moduleName,
                               DWORD timeoutMilliseconds, Logger& log) {
    const ULONGLONG started = GetTickCount64();
    DWORD snapshotError = ERROR_SUCCESS;
    DWORD enumerationError = ERROR_SUCCESS;
    unsigned int attempts = 0;
    for (;;) {
        ++attempts;
        DWORD targetState = WaitForSingleObject(process, 0);
        if (targetState == WAIT_OBJECT_0) {
            log.W(L"Target exited while waiting for remote module: " +
                  moduleName);
            return nullptr;
        }
        if (targetState == WAIT_FAILED) {
            log.Err(L"WaitForRemoteModuleBase: WaitForSingleObject",
                    GetLastError());
            return nullptr;
        }
        if (targetState != WAIT_TIMEOUT) {
            log.W(L"Unexpected target wait state while waiting for remote "
                  L"module: " + std::to_wstring(targetState));
            return nullptr;
        }

        HANDLE snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
        if (snapshot == INVALID_HANDLE_VALUE) {
            snapshotError = GetLastError();
            if (snapshotError != ERROR_BAD_LENGTH &&
                snapshotError != ERROR_PARTIAL_COPY) {
                log.Err(L"WaitForRemoteModuleBase: "
                        L"CreateToolhelp32Snapshot", snapshotError);
                return nullptr;
            }
        } else {
            snapshotError = ERROR_SUCCESS;
            enumerationError = ERROR_SUCCESS;
            MODULEENTRY32W entry{};
            entry.dwSize = sizeof(entry);
            LPVOID result = nullptr;
            if (Module32FirstW(snapshot, &entry)) {
                do {
                    if (_wcsicmp(entry.szModule, moduleName.c_str()) == 0) {
                        result = entry.modBaseAddr;
                        break;
                    }
                } while (Module32NextW(snapshot, &entry));
                if (!result) enumerationError = GetLastError();
            } else {
                enumerationError = GetLastError();
            }
            CloseHandle(snapshot);
            if (result) return result;
        }
        const ULONGLONG elapsed = GetTickCount64() - started;
        if (elapsed >= timeoutMilliseconds) {
            std::wstring diagnostic =
                L"Timed out waiting for remote module: " + moduleName +
                L"; attempts=" + std::to_wstring(attempts) +
                L"; elapsed_ms=" + std::to_wstring(elapsed);
            if (snapshotError != ERROR_SUCCESS) {
                diagnostic += L"; snapshot_error=" +
                    std::to_wstring(snapshotError);
            }
            if (enumerationError != ERROR_SUCCESS &&
                enumerationError != ERROR_NO_MORE_FILES) {
                diagnostic += L"; enumeration_error=" +
                    std::to_wstring(enumerationError);
            }
            log.W(diagnostic);
            return nullptr;
        }
        Sleep(10);
    }
}

InjectResult InjectDll(HANDLE process, DWORD processId,
                       const std::wstring& dllPath,
                       DWORD timeoutMilliseconds, Logger& log) {
    size_t bytes = (dllPath.size() + 1) * sizeof(wchar_t);
    LPVOID remote = VirtualAllocEx(process, nullptr, bytes,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        log.Err(L"InjectDll: VirtualAllocEx", GetLastError());
        return InjectResult::AllocFailed;
    }
    if (!WriteProcessMemory(process, remote, dllPath.c_str(), bytes, nullptr)) {
        log.Err(L"InjectDll: WriteProcessMemory", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::WriteFailed;
    }
    LPTHREAD_START_ROUTINE loadLibrary =
        ResolveRemoteLoadLibraryW(process, processId, log);
    if (!loadLibrary) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::ResolveFailed;
    }
    HANDLE thread = CreateRemoteThread(
        process, nullptr, 0, loadLibrary, remote, 0, nullptr);
    if (!thread) {
        log.Err(L"InjectDll: CreateRemoteThread", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return InjectResult::CreateThreadFailed;
    }
    DWORD wait = WaitForSingleObject(thread, timeoutMilliseconds);
    if (wait != WAIT_OBJECT_0) {
        if (wait != WAIT_TIMEOUT)
            log.Err(L"InjectDll: WaitForSingleObject", GetLastError());
        CloseHandle(thread);
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return wait == WAIT_TIMEOUT ? InjectResult::TimedOut
                                    : InjectResult::CreateThreadFailed;
    }
    DWORD module = 0;
    bool exitRead = GetExitCodeThread(thread, &module) != FALSE;
    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    if (!exitRead || module == 0) return InjectResult::LoadLibraryFailed;
    log.W(L"Injected Reloaded bootstrapper successfully.");
    return InjectResult::Success;
}

BootstrapExitCode RunLaunch(const BootstrapRequest& request, Logger& log) {
#ifndef _WIN64
    (void)request;
    (void)log;
    return BootstrapExitCode::ArchitectureMismatch;
#else
    if (!ValidateRequestPaths(request, log))
        return BootstrapExitCode::InvalidArguments;
    BootstrapExitCode host = ValidateHostFile(
        request.gameExecutable, ExpectedHostArchitecture::X64, log);
    if (host != BootstrapExitCode::Success) return host;

    ValidatedPayload payload;
    if (!ValidatePortablePayload(request, ExpectedHostArchitecture::X64,
                                 payload, log, false))
        return BootstrapExitCode::MissingPayload;
    if (!WriteAppConfig(payload.reloadedRoot,
            request.gameExecutable.filename().wstring(),
            request.gameExecutable, log))
        return BootstrapExitCode::AppConfigFailed;
    ReloadedPointerLease lease(payload.reloadedRoot, log);
    if (!lease.Ready()) return BootstrapExitCode::PointerLeaseUnavailable;

    std::wstring command = L"\"" + request.gameExecutable.wstring() + L"\"";
    if (!request.gameArguments.empty())
        command += L" " + request.gameArguments;
    std::vector<wchar_t> buffer(command.begin(), command.end());
    buffer.push_back(L'\0');
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    {
        ScopedPrivateDotNetEnvironment environment(
            ExpectedHostArchitecture::X64, payload.privateRuntimeRoot, log);
        if (!environment.Applied())
            return BootstrapExitCode::RuntimeUnavailable;
        if (!CreateProcessW(request.gameExecutable.c_str(), buffer.data(),
                nullptr, nullptr, FALSE, CREATE_SUSPENDED, nullptr,
                request.gameExecutable.parent_path().c_str(), &startup,
                &process)) {
            log.Err(L"CreateProcessW(suspended FFVII)", GetLastError());
            return BootstrapExitCode::TargetUnavailable;
        }
    }

    DWORD suspendCount = ResumeThread(process.hThread);
    if (suspendCount == static_cast<DWORD>(-1)) {
        log.Err(L"ResumeThread(loader initialization)", GetLastError());
        TerminateProcess(process.hProcess,
                         static_cast<UINT>(BootstrapExitCode::ResumeFailed));
        WaitForSingleObject(process.hProcess, 5000);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return BootstrapExitCode::ResumeFailed;
    }
    log.W(L"Resumed FFVII for bounded loader initialization; prior suspend count=" +
          std::to_wstring(suspendCount));

    InjectResult injected = InjectDll(process.hProcess, process.dwProcessId,
        payload.bootstrapper.wstring(), 30000, log);
    if (injected != InjectResult::Success) {
        TerminateProcess(process.hProcess,
                         static_cast<UINT>(BootstrapExitCode::InjectionFailed));
        WaitForSingleObject(process.hProcess, 5000);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return BootstrapExitCode::InjectionFailed;
    }
    BootstrapExitCode result = WaitForTarget(process.hProcess, log);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return result;
#endif
}

BootstrapExitCode RunAttach(const BootstrapRequest& request, Logger& log) {
#ifdef _WIN64
    (void)request;
    (void)log;
    return BootstrapExitCode::ArchitectureMismatch;
#else
    if (!ValidateRequestPaths(request, log))
        return BootstrapExitCode::InvalidArguments;
    constexpr DWORD rights = PROCESS_CREATE_THREAD |
        PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE |
        PROCESS_VM_READ | SYNCHRONIZE;
    HANDLE process = OpenProcess(rights, FALSE, request.processId);
    if (!process) {
        log.Err(L"OpenProcess(attach target)", GetLastError());
        return BootstrapExitCode::TargetUnavailable;
    }
    fs::path processPath;
    uint16_t processMachine = 0;
    if (!GetProcessPath(process, processPath, log) ||
        !CanonicalPathsEqual(processPath, request.gameExecutable)) {
        log.W(L"Attach PID and --game path do not identify the same process.");
        CloseHandle(process);
        return BootstrapExitCode::TargetUnavailable;
    }
    if (!GetProcessMachine(process, processMachine, log)) {
        CloseHandle(process);
        return BootstrapExitCode::TargetUnavailable;
    }
    if (processMachine != IMAGE_FILE_MACHINE_I386) {
        CloseHandle(process);
        return BootstrapExitCode::ArchitectureMismatch;
    }
    BootstrapExitCode host = ValidateHostFile(
        request.gameExecutable, ExpectedHostArchitecture::X86, log);
    if (host != BootstrapExitCode::Success) {
        CloseHandle(process);
        return host;
    }
    ValidatedPayload payload;
    if (!ValidatePortablePayload(request, ExpectedHostArchitecture::X86,
                                 payload, log, false)) {
        CloseHandle(process);
        return BootstrapExitCode::MissingPayload;
    }
    if (!WriteAppConfig(payload.reloadedRoot,
            request.gameExecutable.filename().wstring(),
            request.gameExecutable, log)) {
        CloseHandle(process);
        return BootstrapExitCode::AppConfigFailed;
    }
    ReloadedPointerLease lease(payload.reloadedRoot, log);
    if (!lease.Ready()) {
        CloseHandle(process);
        return BootstrapExitCode::PointerLeaseUnavailable;
    }
    if (WaitForSingleObject(process, 0) != WAIT_TIMEOUT) {
        CloseHandle(process);
        return BootstrapExitCode::TargetUnavailable;
    }
    InjectResult injected = InjectDll(process, request.processId,
        payload.bootstrapper.wstring(), 30000, log);
    if (injected != InjectResult::Success) {
        CloseHandle(process);
        return BootstrapExitCode::InjectionFailed;
    }
    if (WaitForSingleObject(process, 0) != WAIT_TIMEOUT) {
        CloseHandle(process);
        return BootstrapExitCode::TargetUnavailable;
    }
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE,
                              request.readyEventName.c_str());
    if (!ready) {
        log.Err(L"OpenEventW(proxy ready event)", GetLastError());
        CloseHandle(process);
        return BootstrapExitCode::ReadySignalFailed;
    }
    bool signaled = SetEvent(ready) != FALSE;
    if (!signaled) log.Err(L"SetEvent(proxy ready event)", GetLastError());
    CloseHandle(ready);
    if (!signaled) {
        CloseHandle(process);
        return BootstrapExitCode::ReadySignalFailed;
    }
    BootstrapExitCode result = WaitForTarget(process, log);
    CloseHandle(process);
    return result;
#endif
}

BootstrapExitCode RunBootstrap(const BootstrapRequest& request, Logger& log) {
#ifdef _WIN64
    if (request.mode != BootstrapMode::Launch)
        return BootstrapExitCode::ArchitectureMismatch;
    return RunLaunch(request, log);
#else
    if (request.mode != BootstrapMode::Attach)
        return BootstrapExitCode::ArchitectureMismatch;
    return RunAttach(request, log);
#endif
}

}  // namespace blind_soldier
