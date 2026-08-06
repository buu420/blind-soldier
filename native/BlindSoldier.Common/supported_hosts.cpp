#include "supported_hosts.h"

#include "supported_hosts.generated.h"

#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <cwctype>

#pragma comment(lib, "bcrypt.lib")

namespace blind_soldier {
namespace {

bool EqualsIgnoreCase(const std::wstring& left, const wchar_t* right) {
    if (!right || left.size() != wcslen(right)) return false;
    for (size_t index = 0; index < left.size(); ++index) {
        if (towlower(left[index]) != towlower(right[index])) return false;
    }
    return true;
}

bool EqualsIgnoreCase(const std::string& left, const char* right) {
    if (!right || left.size() != strlen(right)) return false;
    for (size_t index = 0; index < left.size(); ++index) {
        if (tolower(static_cast<unsigned char>(left[index])) !=
            tolower(static_cast<unsigned char>(right[index]))) return false;
    }
    return true;
}

std::wstring ComputeSha256(const std::vector<uint8_t>& bytes) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::vector<uint8_t> object;
    std::array<uint8_t, 32> digest{};
    DWORD objectLength = 0;
    DWORD returned = 0;

    NTSTATUS status = BCryptOpenAlgorithmProvider(
        &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0);
    if (status < 0) throw std::runtime_error("BCryptOpenAlgorithmProvider failed");
    try {
        status = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
            reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength),
            &returned, 0);
        if (status < 0 || objectLength == 0) {
            throw std::runtime_error("BCryptGetProperty failed");
        }
        object.resize(objectLength);
        status = BCryptCreateHash(algorithm, &hash, object.data(),
                                  objectLength, nullptr, 0, 0);
        if (status < 0) throw std::runtime_error("BCryptCreateHash failed");
        size_t offset = 0;
        while (offset < bytes.size()) {
            size_t remaining = bytes.size() - offset;
            ULONG chunk = remaining > std::numeric_limits<ULONG>::max()
                ? std::numeric_limits<ULONG>::max()
                : static_cast<ULONG>(remaining);
            status = BCryptHashData(hash,
                const_cast<PUCHAR>(bytes.data() + offset), chunk, 0);
            if (status < 0) throw std::runtime_error("BCryptHashData failed");
            offset += chunk;
        }
        status = BCryptFinishHash(hash, digest.data(),
                                  static_cast<ULONG>(digest.size()), 0);
        if (status < 0) throw std::runtime_error("BCryptFinishHash failed");
    } catch (...) {
        if (hash) BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw;
    }
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);

    static constexpr wchar_t kHex[] = L"0123456789ABCDEF";
    std::wstring result;
    result.reserve(digest.size() * 2);
    for (uint8_t value : digest) {
        result.push_back(kHex[value >> 4]);
        result.push_back(kHex[value & 0x0F]);
    }
    return result;
}

bool HasImport(const PeImageInfo& image, const char* module) {
    return std::any_of(image.imports.begin(), image.imports.end(),
        [module](const PeImportInfo& value) {
            return EqualsIgnoreCase(value.module, module);
        });
}

bool MatchesProfile(const PeImageInfo& image,
                    const StructuralHostProfile& profile,
                    std::wstring& failure) {
    if (image.imageBase != profile.imageBase) {
        failure = L"image base differs from profile " + Utf8ToWide(profile.id);
        return false;
    }
    for (const auto& expected : profile.sections) {
        auto section = std::find_if(image.sections.begin(), image.sections.end(),
            [&expected](const PeSectionInfo& actual) {
                return actual.name == expected.name;
            });
        if (section == image.sections.end() ||
            section->rva != expected.rva ||
            section->virtualSize != expected.virtualSize ||
            section->rawSize != expected.rawSize ||
            section->characteristics != expected.characteristics) {
            failure = L"section evidence differs for profile " +
                      Utf8ToWide(profile.id) + L": " +
                      Utf8ToWide(expected.name);
            return false;
        }
    }
    for (const auto& signature : profile.signatures) {
        if (signature.bytes.size() != signature.mask.size() ||
            signature.bytes.empty()) {
            failure = L"generated signature evidence is invalid";
            return false;
        }
        auto offset = image.RvaToFileOffset(signature.rva,
                                            signature.bytes.size());
        if (!offset) {
            failure = L"signature RVA is not file-backed for profile " +
                      Utf8ToWide(profile.id);
            return false;
        }
        for (size_t index = 0; index < signature.bytes.size(); ++index) {
            uint8_t mask = signature.mask[index];
            if ((image.fileBytes[*offset + index] & mask) !=
                (signature.bytes[index] & mask)) {
                failure = L"code signature differs for profile " +
                          Utf8ToWide(profile.id);
                return false;
            }
        }
    }
    return true;
}

}  // namespace

const wchar_t* LegacyStockSha256() {
    return generated::kLegacyStockSha256;
}

const wchar_t* Steam2026Sha256() {
    return generated::kSteam2026Sha256;
}

const std::vector<StructuralHostProfile>& SevenHeavenProfiles() {
    static const std::vector<StructuralHostProfile> profiles = [] {
        std::vector<StructuralHostProfile> result;
        for (const auto& source : generated::kSevenHeavenProfiles) {
            StructuralHostProfile profile;
            profile.id = source.id;
            profile.imageBase = source.imageBase;
            for (size_t index = 0; index < source.sectionCount; ++index) {
                const auto& section = source.sections[index];
                profile.sections.push_back(SectionConstraint{
                    section.name, section.rva, section.virtualSize,
                    section.rawSize, section.characteristics});
            }
            for (size_t index = 0; index < source.signatureCount; ++index) {
                const auto& signature = source.signatures[index];
                profile.signatures.push_back(CodeSignatureConstraint{
                    signature.rva,
                    std::vector<uint8_t>(signature.bytes,
                                         signature.bytes + signature.length),
                    std::vector<uint8_t>(signature.mask,
                                         signature.mask + signature.length)});
            }
            result.push_back(std::move(profile));
        }
        return result;
    }();
    return profiles;
}

HostValidationResult ValidateSupportedHostEvidence(
    const fs::path& executableName,
    ExpectedHostArchitecture expectedArchitecture,
    const PeImageInfo& image,
    const std::wstring& sha256) {
    HostValidationResult result;
    result.sha256 = sha256;
    if (!image.valid) {
        result.diagnostic = image.diagnostic.empty()
            ? L"Executable is not a valid bounded PE image."
            : image.diagnostic;
        return result;
    }

    const uint16_t expectedMachine = expectedArchitecture ==
        ExpectedHostArchitecture::X86
        ? IMAGE_FILE_MACHINE_I386
        : IMAGE_FILE_MACHINE_AMD64;
    if (image.machine != expectedMachine) {
        wchar_t buffer[160];
        swprintf_s(buffer, L"PE machine 0x%04X does not match expected 0x%04X.",
                   image.machine, expectedMachine);
        result.diagnostic = buffer;
        return result;
    }

    std::wstring name = executableName.filename().wstring();
    if (expectedArchitecture == ExpectedHostArchitecture::X64) {
        if (!EqualsIgnoreCase(name, L"FFVII.exe")) {
            result.diagnostic = L"Steam 2026 host must be named FFVII.exe.";
            return result;
        }
        if (sha256 != Steam2026Sha256()) {
            result.diagnostic = L"Steam 2026 SHA-256 is not in the supported allowlist.";
            return result;
        }
        result.kind = SupportedHostKind::Steam2026X64;
        result.supported = true;
        result.diagnostic = L"Supported Steam 2026 x64 FFVII executable.";
        return result;
    }

    bool legacyName = EqualsIgnoreCase(name, L"ff7_en.exe");
    bool sevenHeavenName = legacyName || EqualsIgnoreCase(name, L"ff7.exe");
    if (!sevenHeavenName) {
        result.diagnostic = L"Legacy host must be named ff7_en.exe or ff7.exe.";
        return result;
    }
    if (legacyName && sha256 == LegacyStockSha256()) {
        result.kind = SupportedHostKind::LegacyStockX86;
        result.supported = true;
        result.diagnostic = L"Supported exact stock legacy x86 FFVII executable.";
        return result;
    }
    if (!HasImport(image, "WINMM.DLL")) {
        result.diagnostic = L"Compatible x86 host does not import WINMM.DLL.";
        return result;
    }
    if (image.hasEmbeddedManifest) {
        result.diagnostic = L"Compatible x86 host embeds a manifest that can disable .local WinMM redirection.";
        return result;
    }

    std::wstring lastFailure = L"no structural profile matched";
    for (const auto& profile : SevenHeavenProfiles()) {
        if (MatchesProfile(image, profile, lastFailure)) {
            result.kind = SupportedHostKind::SevenHeavenX86;
            result.supported = true;
            result.diagnostic = L"Supported compatible 7th Heaven x86 FFVII executable (" +
                                Utf8ToWide(profile.id) + L").";
            return result;
        }
    }
    result.diagnostic = L"Compatible x86 structural validation failed: " +
                        lastFailure + L".";
    return result;
}

HostValidationResult ValidateSupportedHost(
    const fs::path& executable,
    ExpectedHostArchitecture expectedArchitecture) {
    PeImageInfo image = InspectPeImage(executable);
    if (!image.valid) {
        HostValidationResult result;
        result.diagnostic = image.diagnostic;
        return result;
    }
    try {
        return ValidateSupportedHostEvidence(
            executable.filename(), expectedArchitecture, image,
            ComputeSha256(image.fileBytes));
    } catch (const std::exception& error) {
        HostValidationResult result;
        result.diagnostic = L"Host SHA-256 calculation failed: " +
                            Utf8ToWide(error.what());
        return result;
    }
}

}  // namespace blind_soldier
