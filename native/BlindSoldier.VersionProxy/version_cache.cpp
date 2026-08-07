#include "version_cache.h"

#include "../BlindSoldier.Common/pe_image.h"
#include "../BlindSoldier.Common/supported_hosts.h"

#include <array>
#include <atomic>

namespace blind_soldier {
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

bool ReadValidatedX86Dll(const fs::path& path,
                         const std::wstring* expectedSha256,
                         std::wstring& actualSha256) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) return false;
    if ((attributes & (FILE_ATTRIBUTE_DIRECTORY |
                       FILE_ATTRIBUTE_REPARSE_POINT)) != 0) {
        return Reject(ERROR_FILE_INVALID);
    }

    const PeImageInfo image = InspectPeImage(path);
    if (!image.valid || image.machine != IMAGE_FILE_MACHINE_I386 ||
        (image.fileCharacteristics & IMAGE_FILE_DLL) == 0) {
        return Reject(ERROR_BAD_EXE_FORMAT);
    }
    try {
        actualSha256 = ComputeSha256(image.fileBytes);
    } catch (...) {
        return Reject(ERROR_INVALID_DATA);
    }
    if (expectedSha256 && actualSha256 != *expectedSha256) {
        return Reject(ERROR_CRC);
    }
    return true;
}

bool ValidateCachedCandidate(const fs::path& path,
                             const std::wstring& expectedSha256) {
    std::wstring actualSha256;
    return ReadValidatedX86Dll(path, &expectedSha256, actualSha256);
}

bool CachedCandidateExists(const fs::path& path, bool& exists) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes != INVALID_FILE_ATTRIBUTES) {
        exists = true;
        return true;
    }
    const DWORD error = GetLastError();
    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND) {
        exists = false;
        return true;
    }
    return false;
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

void DeleteOwnTemporary(const fs::path& temporary, DWORD preservedError) {
    DeleteFileW(temporary.c_str());
    SetLastError(preservedError);
}

}  // namespace

bool BuildCachedSystemVersion(const fs::path& source,
                              const fs::path& cacheDirectory,
                              fs::path& cached) {
    cached.clear();
    std::wstring sourceSha256;
    if (!ReadValidatedX86Dll(source, nullptr, sourceSha256)) return false;
    if (!EnsureDirectory(cacheDirectory)) return false;

    cached = cacheDirectory /
        (L"version-system-x86-" + sourceSha256 + L".dll");

    bool exists = false;
    if (!CachedCandidateExists(cached, exists)) return false;
    if (exists) return ValidateCachedCandidate(cached, sourceSha256);

    fs::path temporary;
    bool copied = false;
    for (size_t attempt = 0; attempt < 64; ++attempt) {
        temporary = UniqueTemporaryPath(cached);
        if (CopyFileW(source.c_str(), temporary.c_str(), TRUE)) {
            copied = true;
            break;
        }
        const DWORD error = GetLastError();
        if (error != ERROR_FILE_EXISTS && error != ERROR_ALREADY_EXISTS) {
            return false;
        }
    }
    if (!copied) return Reject(ERROR_ALREADY_EXISTS);

    if (!ValidateCachedCandidate(temporary, sourceSha256)) {
        const DWORD error = GetLastError();
        DeleteOwnTemporary(temporary, error);
        return false;
    }

    if (MoveFileExW(temporary.c_str(), cached.c_str(),
                    MOVEFILE_WRITE_THROUGH)) {
        return ValidateCachedCandidate(cached, sourceSha256);
    }

    const DWORD publishError = GetLastError();
    DeleteOwnTemporary(temporary, publishError);
    if (publishError == ERROR_ALREADY_EXISTS ||
        publishError == ERROR_FILE_EXISTS) {
        return ValidateCachedCandidate(cached, sourceSha256);
    }
    return false;
}

bool BuildCachedSystemVersion(const fs::path& source,
                              fs::path& cached) {
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
