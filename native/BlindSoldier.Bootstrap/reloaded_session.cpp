#include "reloaded_session.h"

#include <algorithm>

namespace blind_soldier {
namespace {

bool PathComponentEqual(const fs::path& left, const fs::path& right) {
    return _wcsicmp(left.c_str(), right.c_str()) == 0;
}

bool FileHasExpectedMachine(const fs::path& path, uint16_t machine,
                            std::wstring& diagnostic) {
    PeImageInfo image = InspectPeImage(path);
    if (!image.valid) {
        diagnostic = L"Required PE is invalid: " + path.wstring() + L" (" +
                     image.diagnostic + L")";
        return false;
    }
    if (image.machine != machine) {
        diagnostic = L"Required PE has the wrong architecture: " +
                     path.wstring();
        return false;
    }
    return true;
}

bool RequirePayloadFile(const fs::path& canonicalRoot, const fs::path& path,
                        uint16_t machine, bool inspectMachine,
                        fs::path& result, Logger& log) {
    std::wstring diagnostic;
    if (!IsCanonicalPathWithinRoot(canonicalRoot, path, result, diagnostic)) {
        log.W(L"ValidatePortablePayload: " + diagnostic);
        return false;
    }
    std::error_code error;
    if (!fs::is_regular_file(result, error) || error) {
        log.W(L"ValidatePortablePayload: required file is missing: " +
              path.wstring());
        return false;
    }
    if (inspectMachine && !FileHasExpectedMachine(result, machine, diagnostic)) {
        log.W(L"ValidatePortablePayload: " + diagnostic);
        return false;
    }
    return true;
}

std::string ToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    int length = WideCharToMultiByte(CP_UTF8, 0, value.c_str(),
        static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length <= 0) return {};
    std::string result(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(),
        static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    return result;
}

}  // namespace

bool IsCanonicalPathWithinRoot(
    const fs::path& root,
    const fs::path& candidate,
    fs::path& canonicalCandidate,
    std::wstring& diagnostic) {
    diagnostic.clear();
    std::error_code error;
    fs::path canonicalRoot = fs::canonical(root, error);
    if (error) {
        diagnostic = L"Package root is unavailable: " + root.wstring();
        return false;
    }
    canonicalCandidate = fs::canonical(candidate, error);
    if (error) {
        diagnostic = L"Required path is unavailable: " + candidate.wstring();
        return false;
    }
    auto rootPart = canonicalRoot.begin();
    auto candidatePart = canonicalCandidate.begin();
    for (; rootPart != canonicalRoot.end(); ++rootPart, ++candidatePart) {
        if (candidatePart == canonicalCandidate.end() ||
            !PathComponentEqual(*rootPart, *candidatePart)) {
            diagnostic = L"Canonical path escapes package root: " +
                         candidate.wstring();
            return false;
        }
    }
    return true;
}

ReloadedPointerLease::ReloadedPointerLease(
    const fs::path& reloadedRoot, Logger& log, DWORD waitMilliseconds,
    const fs::path& pointerOverride) : log_(&log) {
    pointer_ = pointerOverride.empty() ? ReloadedIIPointerFile()
                                       : pointerOverride;
    if (pointer_.empty()) {
        Fail(L"Reloaded pointer path is unavailable.");
        return;
    }

    unsigned long long hash = 1469598103934665603ULL;
    for (wchar_t character : ToLower(pointer_.wstring())) {
        hash ^= static_cast<unsigned long long>(character);
        hash *= 1099511628211ULL;
    }
    std::wstring mutexName =
        L"Local\\BlindSoldier.ReloadedPointer." + std::to_wstring(hash);
    mutex_ = CreateMutexW(nullptr, FALSE, mutexName.c_str());
    if (!mutex_) {
        Fail(L"Could not create the Reloaded pointer mutex: " +
             Logger::FormatWin32Error(GetLastError()));
        return;
    }
    DWORD wait = WaitForSingleObject(mutex_, waitMilliseconds);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) {
        Fail(wait == WAIT_TIMEOUT
            ? L"Timed out waiting for another Blind Soldier session to release Reloaded."
            : L"Could not acquire the Reloaded pointer mutex: " +
              Logger::FormatWin32Error(GetLastError()));
        return;
    }
    ownsMutex_ = true;
    if (wait == WAIT_ABANDONED)
        log_->A("ReloadedPointerLease: recovering abandoned mutex");

    portableContent_ = ToUtf8(ReloadedIIPointerContent(reloadedRoot));
    if (portableContent_.empty()) {
        Fail(L"Could not encode the Reloaded pointer content.");
        return;
    }
    std::error_code error;
    fs::create_directories(pointer_.parent_path(), error);
    if (error) {
        Fail(L"Could not create the Reloaded pointer directory: " +
             Utf8ToWide(error.message()));
        return;
    }
    backup_ = pointer_;
    backup_ += L".blind_soldier_backup";

    if (fs::exists(backup_, error)) {
        if (fs::exists(pointer_, error)) {
            std::string current;
            if (!ReadUtf8File(pointer_, current) ||
                current != portableContent_) {
                Fail(L"A durable Blind Soldier backup and an externally changed Reloaded pointer both exist.");
                return;
            }
        }
        if (!MoveFileExW(backup_.c_str(), pointer_.c_str(),
                         MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            Fail(L"Could not recover the durable Reloaded pointer backup: " +
                 Logger::FormatWin32Error(GetLastError()));
            return;
        }
        log_->A("ReloadedPointerLease: recovered durable backup");
    } else if (fs::exists(pointer_, error)) {
        std::string current;
        if (ReadUtf8File(pointer_, current) && current == portableContent_) {
            if (!DeleteFileW(pointer_.c_str())) {
                Fail(L"Could not remove an abandoned Blind Soldier pointer: " +
                     Logger::FormatWin32Error(GetLastError()));
                return;
            }
            log_->A("ReloadedPointerLease: removed abandoned pointer");
        }
    }

    if (fs::exists(pointer_, error)) {
        if (!MoveFileExW(pointer_.c_str(), backup_.c_str(),
                         MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            Fail(L"Could not create the durable Reloaded pointer backup: " +
                 Logger::FormatWin32Error(GetLastError()));
            return;
        }
        hadOriginal_ = true;
    }
    if (!WriteReloadedIIPointerAt(pointer_, reloadedRoot, *log_)) {
        RestoreAfterFailedWrite();
        Fail(L"Could not write the temporary Reloaded pointer.");
        return;
    }
    ready_ = true;
    diagnostic_.clear();
}

ReloadedPointerLease::~ReloadedPointerLease() {
    if (ready_ && log_) {
        std::string current;
        bool stillOurs = ReadUtf8File(pointer_, current) &&
                         current == portableContent_;
        if (!stillOurs) {
            log_->A("ReloadedPointerLease: pointer changed externally; preserving it and backup");
        } else if (hadOriginal_) {
            if (MoveFileExW(backup_.c_str(), pointer_.c_str(),
                            MOVEFILE_REPLACE_EXISTING |
                                MOVEFILE_WRITE_THROUGH)) {
                log_->A("ReloadedPointerLease: restored original pointer");
            } else {
                log_->Err(L"ReloadedPointerLease: restore original",
                          GetLastError());
            }
        } else if (DeleteFileW(pointer_.c_str()) ||
                   GetLastError() == ERROR_FILE_NOT_FOUND) {
            log_->A("ReloadedPointerLease: removed temporary pointer");
        } else {
            log_->Err(L"ReloadedPointerLease: remove temporary pointer",
                      GetLastError());
        }
    }
    if (ownsMutex_) ReleaseMutex(mutex_);
    if (mutex_) CloseHandle(mutex_);
}

void ReloadedPointerLease::RestoreAfterFailedWrite() {
    if (!hadOriginal_) return;
    if (MoveFileExW(backup_.c_str(), pointer_.c_str(),
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        log_->A("ReloadedPointerLease: restored original after failed write");
        hadOriginal_ = false;
    } else {
        log_->Err(L"ReloadedPointerLease: restore after failed write",
                  GetLastError());
    }
}

void ReloadedPointerLease::Fail(const std::wstring& diagnostic) {
    diagnostic_ = diagnostic;
    if (log_) log_->W(L"ReloadedPointerLease: " + diagnostic);
}

bool ValidatePortablePayload(
    const BootstrapRequest& request,
    ExpectedHostArchitecture architecture,
    ValidatedPayload& payload,
    Logger& log,
    bool writeAppConfig) {
    payload = {};
    std::error_code error;
    fs::path canonicalRoot = fs::canonical(request.packageRoot, error);
    if (error || !fs::is_directory(canonicalRoot, error)) {
        log.W(L"ValidatePortablePayload: package root is unavailable: " +
              request.packageRoot.wstring());
        return false;
    }
    payload.packageRoot = canonicalRoot;
    payload.reloadedRoot = canonicalRoot / L"Reloaded-II";
    const wchar_t* loaderArch = architecture == ExpectedHostArchitecture::X86
        ? L"X86" : L"X64";
    const wchar_t* modArch = architecture == ExpectedHostArchitecture::X86
        ? L"x86" : L"x64";
    const wchar_t* modAssembly = architecture == ExpectedHostArchitecture::X86
        ? L"Ff7.Accessibility.Reloaded.dll"
        : L"Ff7.Accessibility.Steam2026X64.dll";
    uint16_t machine = architecture == ExpectedHostArchitecture::X86
        ? IMAGE_FILE_MACHINE_I386 : IMAGE_FILE_MACHINE_AMD64;

    fs::path ignored;
    if (!RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"portable.txt", machine, false,
            ignored, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Loader" / loaderArch / L"Bootstrapper" /
                L"Reloaded.Mod.Loader.Bootstrapper.dll",
            machine, true, payload.bootstrapper, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Loader" / loaderArch /
                L"Reloaded.Mod.Loader.dll",
            machine, true, payload.loader, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Mods" / ACCESSIBILITY_MOD_ID /
                L"ModConfig.json",
            machine, false, payload.accessibilityConfig, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Mods" / ACCESSIBILITY_MOD_ID / modArch /
                modAssembly,
            machine, true, payload.accessibilityAssembly, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Mods" / ACCESSIBILITY_MOD_ID / modArch /
                L"prism.dll",
            machine, true, payload.prism, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Mods" / SHARED_HOOKS_MOD_ID /
                L"ModConfig.json",
            machine, false, payload.hooksConfig, log) ||
        !RequirePayloadFile(canonicalRoot,
            payload.reloadedRoot / L"Mods" / SHARED_HOOKS_MOD_ID / modArch /
                L"Reloaded.Hooks.ReloadedII.dll",
            machine, true, payload.hooksAssembly, log)) {
        return false;
    }

    payload.privateRuntimeRoot = canonicalRoot / L"Blind-Soldier" /
        L"Runtime" / L"dotnet" / modArch;
    fs::path hostfxr;
    if (!RequirePayloadFile(canonicalRoot,
            payload.privateRuntimeRoot / L"host" / L"fxr" / L"9.0.8" /
                L"hostfxr.dll",
            machine, true, hostfxr, log)) {
        return false;
    }
    payload.privateRuntimeRoot = hostfxr.parent_path().parent_path()
        .parent_path().parent_path();

    fs::path canonicalGame;
    std::wstring diagnostic;
    if (!IsCanonicalPathWithinRoot(canonicalRoot, request.gameExecutable,
                                   canonicalGame, diagnostic)) {
        log.W(L"ValidatePortablePayload: " + diagnostic);
        return false;
    }
    if (writeAppConfig && !WriteAppConfig(payload.reloadedRoot,
            canonicalGame.filename().wstring(), canonicalGame, log)) {
        return false;
    }
    return true;
}

}  // namespace blind_soldier
