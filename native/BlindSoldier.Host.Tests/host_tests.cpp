#include "../BlindSoldier.Common/supported_hosts.h"

#include <algorithm>
#include <cstdio>

using namespace blind_soldier;

[[noreturn]] static void CheckFailed(const wchar_t* expression,
                                     const char* file, int line) {
    fwprintf(stderr, L"CHECK failed at %hs:%d: %ls\n", file, line,
             expression);
    ExitProcess(100);
}

#define CHECK(expression) \
    do { if (!(expression)) CheckFailed(L#expression, __FILE__, __LINE__); } while (0)

static PeImageInfo MakeStructuralEvidence(const StructuralHostProfile& profile) {
    PeImageInfo image;
    image.valid = true;
    image.machine = IMAGE_FILE_MACHINE_I386;
    image.imageBase = 0x00400000;
    image.hasEmbeddedManifest = false;
    image.imports.push_back(PeImportInfo{"WINMM.DLL", {"timeGetTime"}});

    uint32_t rawOffset = 0x400;
    for (const auto& constraint : profile.sections) {
        PeSectionInfo section;
        section.name = constraint.name;
        section.rva = constraint.rva;
        section.virtualSize = constraint.virtualSize;
        section.rawOffset = rawOffset;
        section.rawSize = std::max<uint32_t>(constraint.rawSize, 0x200);
        section.characteristics = constraint.characteristics;
        image.sections.push_back(section);
        rawOffset += section.rawSize;
    }
    image.fileBytes.resize(rawOffset + 0x400, 0);

    for (const auto& signature : profile.signatures) {
        auto offset = image.RvaToFileOffset(signature.rva, signature.bytes.size());
        CHECK(offset.has_value());
        std::copy(signature.bytes.begin(), signature.bytes.end(),
                  image.fileBytes.begin() + static_cast<ptrdiff_t>(*offset));
    }
    return image;
}

static fs::path NewMalformedPe() {
    fs::path path = fs::temp_directory_path() /
        (L"blind-soldier-malformed-pe-" + std::to_wstring(GetCurrentProcessId()) + L".exe");
    std::vector<uint8_t> bytes(128, 0);
    bytes[0] = 'M';
    bytes[1] = 'Z';
    const uint32_t invalidPeOffset = 0xFFFFFFF0;
    memcpy(bytes.data() + 0x3C, &invalidPeOffset, sizeof(invalidPeOffset));
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    output.close();
    return path;
}

int wmain() {
    PeImageInfo stock;
    stock.valid = true;
    stock.machine = IMAGE_FILE_MACHINE_I386;
    auto stockResult = ValidateSupportedHostEvidence(
        L"ff7_en.exe", ExpectedHostArchitecture::X86, stock,
        LegacyStockSha256());
    CHECK(stockResult.supported);
    CHECK(stockResult.kind == SupportedHostKind::LegacyStockX86);

    const auto& profiles = SevenHeavenProfiles();
    CHECK(!profiles.empty());
    CHECK(profiles.front().signatures.size() >= 3);
    auto converted = MakeStructuralEvidence(profiles.front());
    for (const auto* name : {L"ff7.exe", L"ff7_en.exe"}) {
        auto result = ValidateSupportedHostEvidence(
            name, ExpectedHostArchitecture::X86, converted,
            L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        CHECK(result.supported);
        CHECK(result.kind == SupportedHostKind::SevenHeavenX86);
    }

    CHECK(!ValidateSupportedHostEvidence(
        L"renamed.exe", ExpectedHostArchitecture::X86, converted,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    auto wrongMachine = converted;
    wrongMachine.machine = IMAGE_FILE_MACHINE_AMD64;
    CHECK(!ValidateSupportedHostEvidence(
        L"ff7.exe", ExpectedHostArchitecture::X86, wrongMachine,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    CHECK(!ValidateSupportedHostEvidence(
        L"ff7_en.exe", ExpectedHostArchitecture::X86, stock,
        L"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB").supported);

    auto missingWinMm = converted;
    missingWinMm.imports.clear();
    CHECK(!ValidateSupportedHostEvidence(
        L"ff7.exe", ExpectedHostArchitecture::X86, missingWinMm,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    auto alteredSignature = converted;
    const auto& signature = profiles.front().signatures.front();
    auto alteredOffset = alteredSignature.RvaToFileOffset(signature.rva, signature.bytes.size());
    CHECK(alteredOffset.has_value());
    alteredSignature.fileBytes[*alteredOffset] ^= 0xFF;
    CHECK(!ValidateSupportedHostEvidence(
        L"ff7.exe", ExpectedHostArchitecture::X86, alteredSignature,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    auto manifest = converted;
    manifest.hasEmbeddedManifest = true;
    CHECK(!ValidateSupportedHostEvidence(
        L"ff7.exe", ExpectedHostArchitecture::X86, manifest,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    PeImageInfo steam;
    steam.valid = true;
    steam.machine = IMAGE_FILE_MACHINE_AMD64;
    auto steamResult = ValidateSupportedHostEvidence(
        L"FFVII.exe", ExpectedHostArchitecture::X64, steam,
        Steam2026Sha256());
    CHECK(steamResult.supported);
    CHECK(steamResult.kind == SupportedHostKind::Steam2026X64);

    auto gogResult = ValidateSupportedHostEvidence(
        L"FFVII.exe", ExpectedHostArchitecture::X64, steam,
        L"458539466A971406C5BFA2D02645DE7E5D6DC536463BC6434ECF9EDAB6C527A6");
    CHECK(gogResult.supported);
    CHECK(gogResult.kind == SupportedHostKind::Steam2026X64);
    CHECK(!ValidateSupportedHostEvidence(
        L"FFVII.exe", ExpectedHostArchitecture::X64, steam,
        L"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").supported);

    auto malformedPath = NewMalformedPe();
    auto malformed = ValidateSupportedHost(malformedPath, ExpectedHostArchitecture::X86);
    CHECK(!malformed.supported);
    CHECK(!malformed.diagnostic.empty());
    std::error_code ignored;
    fs::remove(malformedPath, ignored);

    const fs::path patchPath =
        L"C:\\Users\\buu42\\Tools\\7thHeaven\\Resources\\FF7_1.02_Eng_Patch\\ff7.exe";
    const fs::path convertedPath =
        L"C:\\Users\\buu42\\ff7_accessibility_analysis\\input\\ff7_en.exe";
    const fs::path steamPath =
        L"C:\\Program Files (x86)\\Steam\\steamapps\\common\\FINAL FANTASY VII Steam Edition\\FFVII.exe";
    if (fs::exists(patchPath)) {
        auto result = ValidateSupportedHost(patchPath, ExpectedHostArchitecture::X86);
        CHECK(result.supported);
        CHECK(result.kind == SupportedHostKind::SevenHeavenX86);
    }
    if (fs::exists(convertedPath)) {
        auto result = ValidateSupportedHost(convertedPath, ExpectedHostArchitecture::X86);
        CHECK(result.supported);
        CHECK(result.kind == SupportedHostKind::SevenHeavenX86);
    }
    if (fs::exists(steamPath)) {
        auto result = ValidateSupportedHost(steamPath, ExpectedHostArchitecture::X64);
        CHECK(result.supported);
        CHECK(result.kind == SupportedHostKind::Steam2026X64);
    }

    // The unmodified Steam 2013 ff7_en.exe (app 39140, build 115956) is the
    // converted-output profile's sample. Point this at a private copy; the
    // French, German and Spanish names stay outside the supported boundary.
    wchar_t stock2013[MAX_PATH]{};
    const DWORD stock2013Length = GetEnvironmentVariableW(
        L"BLIND_SOLDIER_STOCK_2013_FF7_EN", stock2013, MAX_PATH);
    if (stock2013Length > 0 && stock2013Length < MAX_PATH) {
        const fs::path stock2013Path(stock2013);
        auto result = ValidateSupportedHost(stock2013Path, ExpectedHostArchitecture::X86);
        CHECK(result.supported);
        CHECK(result.kind == SupportedHostKind::SevenHeavenX86);
        CHECK(result.sha256 ==
              L"68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2");
        CHECK(result.diagnostic.find(L"7th-heaven-converted-output") != std::wstring::npos);
        auto image = InspectPeImage(stock2013Path);
        for (const auto* name : {L"ff7_fr.exe", L"ff7_de.exe", L"ff7_es.exe"}) {
            CHECK(!ValidateSupportedHostEvidence(
                name, ExpectedHostArchitecture::X86, image, result.sha256).supported);
        }
        fwprintf(stdout, L"Stock Steam 2013 ff7_en.exe: %ls\n", result.diagnostic.c_str());
    }

    fwprintf(stdout, L"Blind Soldier host validation tests passed.\n");
    return 0;
}
