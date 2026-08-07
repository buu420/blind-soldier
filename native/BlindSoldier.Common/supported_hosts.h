#pragma once

#include "pe_image.h"

namespace blind_soldier {

enum class SupportedHostKind {
    None,
    LegacyStockX86,
    SevenHeavenX86,
    Steam2026X64
};

enum class ExpectedHostArchitecture { X86, X64 };

struct SectionConstraint {
    std::string name;
    uint32_t rva = 0;
    uint32_t virtualSize = 0;
    uint32_t rawSize = 0;
    uint32_t characteristics = 0;
};

struct CodeSignatureConstraint {
    uint32_t rva = 0;
    std::vector<uint8_t> bytes;
    std::vector<uint8_t> mask;
};

struct StructuralHostProfile {
    std::string id;
    uint64_t imageBase = 0;
    std::vector<SectionConstraint> sections;
    std::vector<CodeSignatureConstraint> signatures;
};

struct HostValidationResult {
    SupportedHostKind kind = SupportedHostKind::None;
    bool supported = false;
    std::wstring diagnostic;
    std::wstring sha256;
};

const wchar_t* LegacyStockSha256();
const wchar_t* Steam2026Sha256();
const std::vector<StructuralHostProfile>& SevenHeavenProfiles();

std::wstring ComputeSha256(const std::vector<uint8_t>& bytes);

HostValidationResult ValidateSupportedHostEvidence(
    const fs::path& executableName,
    ExpectedHostArchitecture expectedArchitecture,
    const PeImageInfo& image,
    const std::wstring& sha256);

HostValidationResult ValidateSupportedHost(
    const fs::path& executable,
    ExpectedHostArchitecture expectedArchitecture);

}  // namespace blind_soldier
