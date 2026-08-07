#pragma once

#include "../BlindSoldier.Common/common.h"

namespace blind_soldier {

enum class VersionCachePublicationResult {
    None,
    Existing,
    Published,
    RaceWinner,
};

using VersionCacheBeforePublish = bool (*)(void* context);
using VersionCacheCopyFile = bool (*)(const fs::path& source,
                                      const fs::path& destination,
                                      void* context);
using VersionCacheMoveFile = bool (*)(const fs::path& source,
                                      const fs::path& destination,
                                      void* context);
using VersionCacheDeleteFile = bool (*)(const fs::path& path,
                                        void* context);

struct VersionCacheBuildOptions {
    void* context = nullptr;
    VersionCacheBeforePublish beforePublish = nullptr;
    VersionCacheCopyFile copyFile = nullptr;
    VersionCacheMoveFile moveFile = nullptr;
    VersionCacheDeleteFile deleteFile = nullptr;
    VersionCachePublicationResult* publicationResult = nullptr;
};

struct VersionCacheLeaseFactory;

class ValidatedVersionCacheLease final {
public:
    ValidatedVersionCacheLease() noexcept = default;
    ~ValidatedVersionCacheLease();
    ValidatedVersionCacheLease(ValidatedVersionCacheLease&& other) noexcept;
    ValidatedVersionCacheLease& operator=(
        ValidatedVersionCacheLease&& other) noexcept;

    ValidatedVersionCacheLease(const ValidatedVersionCacheLease&) = delete;
    ValidatedVersionCacheLease& operator=(
        const ValidatedVersionCacheLease&) = delete;

    explicit operator bool() const noexcept {
        return handle_ != INVALID_HANDLE_VALUE;
    }
    const fs::path& path() const noexcept { return path_; }
    HANDLE handle() const noexcept { return handle_; }
    void Reset() noexcept;

private:
    friend struct VersionCacheLeaseFactory;
    fs::path path_;
    HANDLE handle_ = INVALID_HANDLE_VALUE;
};

bool BuildCachedSystemVersion(
    const fs::path& source,
    const fs::path& cacheDirectory,
    ValidatedVersionCacheLease& cached,
    const VersionCacheBuildOptions* options = nullptr);

bool BuildCachedSystemVersion(const fs::path& source,
                              ValidatedVersionCacheLease& cached);

}  // namespace blind_soldier
