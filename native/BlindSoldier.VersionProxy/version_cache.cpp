#include "version_cache.h"

#include "../BlindSoldier.Common/pe_image.h"
#include "../BlindSoldier.Common/supported_hosts.h"

#include <array>
#include <atomic>
#include <utility>

namespace blind_soldier {

struct VersionCacheLeaseFactory {
    static void Adopt(ValidatedVersionCacheLease& lease,
                      const fs::path& path,
                      HANDLE handle) {
        lease.Reset();
        lease.path_ = path;
        lease.handle_ = handle;
    }
};

ValidatedVersionCacheLease::~ValidatedVersionCacheLease() {
    Reset();
}

ValidatedVersionCacheLease::ValidatedVersionCacheLease(
    ValidatedVersionCacheLease&& other) noexcept
    : path_(std::move(other.path_)), handle_(other.handle_) {
    other.handle_ = INVALID_HANDLE_VALUE;
    other.path_.clear();
}

ValidatedVersionCacheLease& ValidatedVersionCacheLease::operator=(
    ValidatedVersionCacheLease&& other) noexcept {
    if (this == &other) return *this;
    Reset();
    path_ = std::move(other.path_);
    handle_ = other.handle_;
    other.handle_ = INVALID_HANDLE_VALUE;
    other.path_.clear();
    return *this;
}

void ValidatedVersionCacheLease::Reset() noexcept {
    const DWORD preservedError = GetLastError();
    if (handle_ != INVALID_HANDLE_VALUE) CloseHandle(handle_);
    handle_ = INVALID_HANDLE_VALUE;
    path_.clear();
    SetLastError(preservedError);
}

namespace {

bool EnsureDirectory(const fs::path& path) {
    if (CreateDirectoryW(path.c_str(), nullptr)) return true;
    if (GetLastError() != ERROR_ALREADY_EXISTS) return false;
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
        (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool Reject(DWORD error) {
    SetLastError(error);
    return false;
}

bool OpenValidatedX86Dll(const fs::path& path,
                         const std::wstring* expectedSha256,
                         ValidatedVersionCacheLease& lease,
                         std::wstring& actualSha256) {
    lease.Reset();
    HANDLE handle = CreateFileW(
        path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_BACKUP_SEMANTICS |
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr);
    if (handle == INVALID_HANDLE_VALUE) return false;

    FILE_ATTRIBUTE_TAG_INFO tagInfo{};
    if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo,
                                      &tagInfo, sizeof(tagInfo))) {
        const DWORD error = GetLastError();
        CloseHandle(handle);
        return Reject(error);
    }
    if (GetFileType(handle) != FILE_TYPE_DISK ||
        (tagInfo.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY |
                                   FILE_ATTRIBUTE_REPARSE_POINT)) != 0) {
        CloseHandle(handle);
        return Reject(ERROR_FILE_INVALID);
    }

    const PeImageInfo image = InspectPeImage(path);
    if (!image.valid || image.machine != IMAGE_FILE_MACHINE_I386 ||
        (image.fileCharacteristics & IMAGE_FILE_DLL) == 0) {
        CloseHandle(handle);
        return Reject(ERROR_BAD_EXE_FORMAT);
    }
    try {
        actualSha256 = ComputeSha256(image.fileBytes);
    } catch (...) {
        CloseHandle(handle);
        return Reject(ERROR_INVALID_DATA);
    }
    if (expectedSha256 && actualSha256 != *expectedSha256) {
        CloseHandle(handle);
        return Reject(ERROR_CRC);
    }

    VersionCacheLeaseFactory::Adopt(lease, path, handle);
    return true;
}

fs::path UniqueTemporaryPath(const fs::path& cached) {
    static std::atomic<unsigned long long> sequence{0};
    const auto value = sequence.fetch_add(1, std::memory_order_relaxed);
    return cached.parent_path() /
        (cached.filename().wstring() + L"." +
         std::to_wstring(GetCurrentProcessId()) + L"." +
         std::to_wstring(GetCurrentThreadId()) + L"." +
         std::to_wstring(value) + L".tmp");
}

bool CopyCandidate(const fs::path& source,
                   const fs::path& temporary,
                   const VersionCacheBuildOptions* options) {
    if (options && options->copyFile) {
        return options->copyFile(source, temporary, options->context);
    }
    return CopyFileW(source.c_str(), temporary.c_str(), TRUE) != FALSE;
}

bool PublishCandidate(const fs::path& temporary,
                      const fs::path& cached,
                      const VersionCacheBuildOptions* options) {
    if (options && options->moveFile) {
        return options->moveFile(temporary, cached, options->context);
    }
    return MoveFileExW(temporary.c_str(), cached.c_str(),
                       MOVEFILE_WRITE_THROUGH) != FALSE;
}

bool CleanupTemporary(const fs::path& temporary,
                      const VersionCacheBuildOptions* options) {
    const bool deleted = options && options->deleteFile
        ? options->deleteFile(temporary, options->context)
        : DeleteFileW(temporary.c_str()) != FALSE;
    if (deleted) return true;
    const DWORD error = GetLastError();
    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND) {
        return true;
    }
    SetLastError(error);
    return false;
}

void SetPublicationResult(const VersionCacheBuildOptions* options,
                          VersionCachePublicationResult result) {
    if (options && options->publicationResult) {
        *options->publicationResult = result;
    }
}

bool ReturnWithCleanup(const fs::path& temporary,
                       const VersionCacheBuildOptions* options,
                       DWORD primaryError) {
    if (!CleanupTemporary(temporary, options)) return false;
    SetLastError(primaryError);
    return false;
}

}  // namespace

bool BuildCachedSystemVersion(
    const fs::path& source,
    const fs::path& cacheDirectory,
    ValidatedVersionCacheLease& cached,
    const VersionCacheBuildOptions* options) {
    cached.Reset();
    SetPublicationResult(options, VersionCachePublicationResult::None);

    ValidatedVersionCacheLease sourceLease;
    std::wstring sourceSha256;
    if (!OpenValidatedX86Dll(source, nullptr, sourceLease, sourceSha256)) {
        return false;
    }
    if (!EnsureDirectory(cacheDirectory)) return false;

    const fs::path cachedPath = cacheDirectory /
        (L"version-system-x86-" + sourceSha256 + L".dll");
    std::wstring candidateSha256;
    ValidatedVersionCacheLease existingLease;
    if (OpenValidatedX86Dll(cachedPath, &sourceSha256,
                            existingLease, candidateSha256)) {
        cached = std::move(existingLease);
        SetPublicationResult(options, VersionCachePublicationResult::Existing);
        SetLastError(ERROR_SUCCESS);
        return true;
    }
    const DWORD existingError = GetLastError();
    if (existingError != ERROR_FILE_NOT_FOUND &&
        existingError != ERROR_PATH_NOT_FOUND) {
        return false;
    }

    fs::path temporary;
    bool copied = false;
    for (size_t attempt = 0; attempt < 64; ++attempt) {
        temporary = UniqueTemporaryPath(cachedPath);
        if (CopyCandidate(source, temporary, options)) {
            copied = true;
            break;
        }
        const DWORD copyError = GetLastError();
        if (copyError == ERROR_FILE_EXISTS ||
            copyError == ERROR_ALREADY_EXISTS) {
            continue;
        }
        return ReturnWithCleanup(temporary, options, copyError);
    }
    if (!copied) return Reject(ERROR_ALREADY_EXISTS);

    ValidatedVersionCacheLease temporaryLease;
    candidateSha256.clear();
    if (!OpenValidatedX86Dll(temporary, &sourceSha256,
                             temporaryLease, candidateSha256)) {
        const DWORD validationError = GetLastError();
        temporaryLease.Reset();
        return ReturnWithCleanup(temporary, options, validationError);
    }
    temporaryLease.Reset();

    if (options && options->beforePublish &&
        !options->beforePublish(options->context)) {
        DWORD callbackError = GetLastError();
        if (callbackError == ERROR_SUCCESS) callbackError = ERROR_CANCELLED;
        return ReturnWithCleanup(temporary, options, callbackError);
    }

    if (PublishCandidate(temporary, cachedPath, options)) {
        ValidatedVersionCacheLease publishedLease;
        candidateSha256.clear();
        if (!OpenValidatedX86Dll(cachedPath, &sourceSha256,
                                 publishedLease, candidateSha256)) {
            return false;
        }
        cached = std::move(publishedLease);
        SetPublicationResult(options,
                             VersionCachePublicationResult::Published);
        SetLastError(ERROR_SUCCESS);
        return true;
    }

    const DWORD publishError = GetLastError();
    if (!CleanupTemporary(temporary, options)) return false;
    if (publishError != ERROR_ALREADY_EXISTS &&
        publishError != ERROR_FILE_EXISTS) {
        return Reject(publishError);
    }

    ValidatedVersionCacheLease winnerLease;
    candidateSha256.clear();
    if (!OpenValidatedX86Dll(cachedPath, &sourceSha256,
                             winnerLease, candidateSha256)) {
        return false;
    }
    cached = std::move(winnerLease);
    SetPublicationResult(options, VersionCachePublicationResult::RaceWinner);
    SetLastError(ERROR_ALREADY_EXISTS);
    return true;
}

bool BuildCachedSystemVersion(const fs::path& source,
                              ValidatedVersionCacheLease& cached) {
    std::array<wchar_t, 32768> localAppData{};
    const DWORD localLength = GetEnvironmentVariableW(
        L"LOCALAPPDATA", localAppData.data(),
        static_cast<DWORD>(localAppData.size()));
    if (localLength == 0 || localLength >= localAppData.size()) return false;

    const fs::path productDirectory =
        fs::path(std::wstring(localAppData.data(), localLength)) /
        L"Blind Soldier";
    const fs::path cacheDirectory = productDirectory / L"NativeCache";
    if (!EnsureDirectory(productDirectory)) return false;
    return BuildCachedSystemVersion(source, cacheDirectory, cached);
}

}  // namespace blind_soldier
