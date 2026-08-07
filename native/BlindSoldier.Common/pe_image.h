#pragma once

#include "common.h"

#include <cstdint>
#include <optional>

namespace blind_soldier {

struct PeSectionInfo {
    std::string name;
    uint32_t rva = 0;
    uint32_t virtualSize = 0;
    uint32_t rawOffset = 0;
    uint32_t rawSize = 0;
    uint32_t characteristics = 0;
};

struct PeImportInfo {
    std::string module;
    std::vector<std::string> symbols;
};

struct PeImageInfo {
    bool valid = false;
    uint16_t machine = 0;
    uint16_t fileCharacteristics = 0;
    uint64_t imageBase = 0;
    uint32_t sizeOfHeaders = 0;
    bool hasEmbeddedManifest = false;
    std::vector<PeSectionInfo> sections;
    std::vector<PeImportInfo> imports;
    std::vector<uint8_t> fileBytes;
    std::wstring diagnostic;

    std::optional<size_t> RvaToFileOffset(uint32_t rva,
                                          size_t length) const;
};

PeImageInfo InspectPeImage(const fs::path& path);

}  // namespace blind_soldier
