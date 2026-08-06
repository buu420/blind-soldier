#include "pe_image.h"

#include <algorithm>
#include <cstring>
#include <limits>

namespace blind_soldier {
namespace {

template <typename T>
bool ReadValue(const std::vector<uint8_t>& bytes, size_t offset, T& value) {
    if (offset > bytes.size() || sizeof(T) > bytes.size() - offset) {
        return false;
    }
    memcpy(&value, bytes.data() + offset, sizeof(T));
    return true;
}

bool CheckedAdd(size_t left, size_t right, size_t& result) {
    if (left > std::numeric_limits<size_t>::max() - right) return false;
    result = left + right;
    return true;
}

bool ReadAsciiString(const PeImageInfo& image, uint32_t rva,
                     size_t maximumLength, std::string& result) {
    auto offset = image.RvaToFileOffset(rva, 1);
    if (!offset) return false;
    result.clear();
    for (size_t index = 0; index < maximumLength; ++index) {
        size_t current = 0;
        if (!CheckedAdd(*offset, index, current) ||
            current >= image.fileBytes.size()) {
            return false;
        }
        char value = static_cast<char>(image.fileBytes[current]);
        if (value == '\0') return !result.empty();
        result.push_back(value);
    }
    return false;
}

bool ParseImports(PeImageInfo& image, uint32_t directoryRva,
                  uint32_t directorySize, bool is64Bit,
                  std::wstring& diagnostic) {
    if (directoryRva == 0 || directorySize == 0) return true;
    constexpr size_t kDescriptorSize = 20;
    size_t maximumDescriptors = std::min<size_t>(directorySize / kDescriptorSize,
                                                 4096);
    if (maximumDescriptors == 0) {
        diagnostic = L"PE import directory is smaller than one descriptor.";
        return false;
    }

    bool terminated = false;
    for (size_t index = 0; index < maximumDescriptors; ++index) {
        uint64_t descriptorRva64 = static_cast<uint64_t>(directoryRva) +
                                   index * kDescriptorSize;
        if (descriptorRva64 > std::numeric_limits<uint32_t>::max()) {
            diagnostic = L"PE import descriptor RVA overflowed.";
            return false;
        }
        auto offset = image.RvaToFileOffset(
            static_cast<uint32_t>(descriptorRva64), kDescriptorSize);
        if (!offset) {
            diagnostic = L"PE import descriptor is outside file-backed data.";
            return false;
        }

        uint32_t originalFirstThunk = 0;
        uint32_t nameRva = 0;
        uint32_t firstThunk = 0;
        if (!ReadValue(image.fileBytes, *offset, originalFirstThunk) ||
            !ReadValue(image.fileBytes, *offset + 12, nameRva) ||
            !ReadValue(image.fileBytes, *offset + 16, firstThunk)) {
            diagnostic = L"PE import descriptor is truncated.";
            return false;
        }
        if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0) {
            terminated = true;
            break;
        }

        PeImportInfo import;
        if (!ReadAsciiString(image, nameRva, 512, import.module)) {
            diagnostic = L"PE import module name is invalid or unterminated.";
            return false;
        }

        uint32_t thunkRva = originalFirstThunk != 0
            ? originalFirstThunk
            : firstThunk;
        const size_t thunkSize = is64Bit ? 8 : 4;
        bool thunkTerminated = false;
        for (size_t thunkIndex = 0; thunkIndex < 65536; ++thunkIndex) {
            uint64_t currentRva64 = static_cast<uint64_t>(thunkRva) +
                                    thunkIndex * thunkSize;
            if (currentRva64 > std::numeric_limits<uint32_t>::max()) {
                diagnostic = L"PE import thunk RVA overflowed.";
                return false;
            }
            auto thunkOffset = image.RvaToFileOffset(
                static_cast<uint32_t>(currentRva64), thunkSize);
            if (!thunkOffset) {
                diagnostic = L"PE import thunk is outside file-backed data.";
                return false;
            }

            uint64_t thunk = 0;
            if (is64Bit) {
                if (!ReadValue(image.fileBytes, *thunkOffset, thunk)) return false;
            } else {
                uint32_t thunk32 = 0;
                if (!ReadValue(image.fileBytes, *thunkOffset, thunk32)) return false;
                thunk = thunk32;
            }
            if (thunk == 0) {
                thunkTerminated = true;
                break;
            }

            const uint64_t ordinalFlag = is64Bit
                ? 0x8000000000000000ULL
                : 0x80000000ULL;
            if ((thunk & ordinalFlag) != 0) {
                import.symbols.push_back("#" +
                    std::to_string(static_cast<uint16_t>(thunk & 0xFFFF)));
                continue;
            }
            if (thunk > std::numeric_limits<uint32_t>::max()) {
                diagnostic = L"PE import-by-name RVA is too large.";
                return false;
            }
            uint32_t symbolRva = static_cast<uint32_t>(thunk);
            if (symbolRva > std::numeric_limits<uint32_t>::max() - 2) {
                diagnostic = L"PE import-by-name RVA overflowed.";
                return false;
            }
            std::string symbol;
            if (!ReadAsciiString(image, symbolRva + 2, 1024, symbol)) {
                diagnostic = L"PE import symbol is invalid or unterminated.";
                return false;
            }
            import.symbols.push_back(std::move(symbol));
        }
        if (!thunkTerminated) {
            diagnostic = L"PE import thunk table has no bounded terminator.";
            return false;
        }
        image.imports.push_back(std::move(import));
    }

    if (!terminated) {
        diagnostic = L"PE import directory has no bounded terminator.";
        return false;
    }
    return true;
}

bool DetectManifest(const PeImageInfo& image, uint32_t resourceRva,
                    uint32_t resourceSize, bool& result,
                    std::wstring& diagnostic) {
    result = false;
    if (resourceRva == 0 || resourceSize == 0) return true;
    if (resourceSize < 16) {
        diagnostic = L"PE resource directory is smaller than its root header.";
        return false;
    }
    auto root = image.RvaToFileOffset(resourceRva, resourceSize);
    if (!root) {
        diagnostic = L"PE resource root is outside file-backed data.";
        return false;
    }
    uint16_t namedEntries = 0;
    uint16_t idEntries = 0;
    if (!ReadValue(image.fileBytes, *root + 12, namedEntries) ||
        !ReadValue(image.fileBytes, *root + 14, idEntries)) {
        diagnostic = L"PE resource root is truncated.";
        return false;
    }
    size_t count = static_cast<size_t>(namedEntries) + idEntries;
    if (count > 65535 || count > (resourceSize - 16) / 8) {
        diagnostic = L"PE resource entry count is outside the declared resource directory.";
        return false;
    }
    for (size_t index = 0; index < count; ++index) {
        size_t entryOffset = 0;
        if (!CheckedAdd(*root + 16, index * 8, entryOffset) ||
            entryOffset > image.fileBytes.size() - 8) {
            diagnostic = L"PE resource entry is truncated.";
            return false;
        }
        uint32_t name = 0;
        if (!ReadValue(image.fileBytes, entryOffset, name)) return false;
        if ((name & 0x80000000U) == 0 && (name & 0xFFFFU) == 24U) {
            result = true;
            return true;
        }
    }
    return true;
}

}  // namespace

std::optional<size_t> PeImageInfo::RvaToFileOffset(uint32_t rva,
                                                   size_t length) const {
    if (rva < sizeOfHeaders) {
        size_t offset = static_cast<size_t>(rva);
        if (offset <= fileBytes.size() && length <= fileBytes.size() - offset) {
            return offset;
        }
    }
    for (const auto& section : sections) {
        uint64_t sectionEnd = static_cast<uint64_t>(section.rva) +
                              section.rawSize;
        uint64_t requestedEnd = static_cast<uint64_t>(rva) + length;
        if (rva < section.rva || requestedEnd > sectionEnd) continue;
        uint64_t offset64 = static_cast<uint64_t>(section.rawOffset) +
                            (rva - section.rva);
        if (offset64 > fileBytes.size() ||
            length > fileBytes.size() - static_cast<size_t>(offset64)) {
            return std::nullopt;
        }
        return static_cast<size_t>(offset64);
    }
    return std::nullopt;
}

PeImageInfo InspectPeImage(const fs::path& path) {
    PeImageInfo image;
    try {
        std::ifstream input(path, std::ios::binary);
        if (!input) {
            image.diagnostic = L"Unable to open executable: " + path.wstring();
            return image;
        }
        image.fileBytes.assign(std::istreambuf_iterator<char>(input),
                               std::istreambuf_iterator<char>());
        if (image.fileBytes.size() < 64) {
            image.diagnostic = L"Executable is smaller than a DOS header.";
            return image;
        }

        uint16_t mz = 0;
        uint32_t peOffset = 0;
        if (!ReadValue(image.fileBytes, 0, mz) || mz != 0x5A4D ||
            !ReadValue(image.fileBytes, 0x3C, peOffset)) {
            image.diagnostic = L"Executable has an invalid DOS header.";
            return image;
        }
        uint32_t signature = 0;
        size_t coffOffset = 0;
        if (!CheckedAdd(static_cast<size_t>(peOffset), 4, coffOffset) ||
            !ReadValue(image.fileBytes, peOffset, signature) ||
            signature != 0x00004550) {
            image.diagnostic = L"Executable has an invalid or out-of-range PE header.";
            return image;
        }

        uint16_t sectionCount = 0;
        uint16_t optionalSize = 0;
        if (!ReadValue(image.fileBytes, coffOffset, image.machine) ||
            !ReadValue(image.fileBytes, coffOffset + 2, sectionCount) ||
            !ReadValue(image.fileBytes, coffOffset + 16, optionalSize)) {
            image.diagnostic = L"Executable has a truncated COFF header.";
            return image;
        }
        if (sectionCount == 0 || sectionCount > 96) {
            image.diagnostic = L"Executable has an invalid PE section count.";
            return image;
        }

        size_t optionalOffset = coffOffset + 20;
        if (optionalOffset > image.fileBytes.size() ||
            optionalSize > image.fileBytes.size() - optionalOffset) {
            image.diagnostic = L"Executable optional header is outside the file.";
            return image;
        }
        uint16_t magic = 0;
        if (!ReadValue(image.fileBytes, optionalOffset, magic)) return image;
        bool is64Bit = magic == 0x20B;
        if (magic == 0x10B) {
            uint32_t base32 = 0;
            if (optionalSize < 96 ||
                !ReadValue(image.fileBytes, optionalOffset + 28, base32)) {
                image.diagnostic = L"Executable has a truncated PE32 optional header.";
                return image;
            }
            image.imageBase = base32;
        } else if (is64Bit) {
            if (optionalSize < 112 ||
                !ReadValue(image.fileBytes, optionalOffset + 24, image.imageBase)) {
                image.diagnostic = L"Executable has a truncated PE32+ optional header.";
                return image;
            }
        } else {
            image.diagnostic = L"Executable has an unsupported optional-header magic.";
            return image;
        }
        if (!ReadValue(image.fileBytes, optionalOffset + 60, image.sizeOfHeaders)) {
            image.diagnostic = L"Executable has no SizeOfHeaders value.";
            return image;
        }

        size_t sectionTable = optionalOffset + optionalSize;
        size_t sectionTableSize = static_cast<size_t>(sectionCount) * 40;
        if (sectionTable > image.fileBytes.size() ||
            sectionTableSize > image.fileBytes.size() - sectionTable) {
            image.diagnostic = L"Executable section table is outside the file.";
            return image;
        }
        for (size_t index = 0; index < sectionCount; ++index) {
            size_t offset = sectionTable + index * 40;
            PeSectionInfo section;
            char name[9]{};
            memcpy(name, image.fileBytes.data() + offset, 8);
            section.name = name;
            if (!ReadValue(image.fileBytes, offset + 8, section.virtualSize) ||
                !ReadValue(image.fileBytes, offset + 12, section.rva) ||
                !ReadValue(image.fileBytes, offset + 16, section.rawSize) ||
                !ReadValue(image.fileBytes, offset + 20, section.rawOffset) ||
                !ReadValue(image.fileBytes, offset + 36, section.characteristics)) {
                image.diagnostic = L"Executable section table is truncated.";
                return image;
            }
            if (section.rawSize != 0 &&
                (section.rawOffset > image.fileBytes.size() ||
                 section.rawSize > image.fileBytes.size() - section.rawOffset)) {
                image.diagnostic = L"Executable section raw range is outside the file.";
                return image;
            }
            image.sections.push_back(std::move(section));
        }

        size_t directoriesOffset = optionalOffset + (is64Bit ? 112 : 96);
        if (directoriesOffset > optionalOffset + optionalSize ||
            16 * 8 > optionalOffset + optionalSize - directoriesOffset) {
            image.diagnostic = L"Executable data-directory table is truncated.";
            return image;
        }
        uint32_t importRva = 0, importSize = 0;
        uint32_t resourceRva = 0, resourceSize = 0;
        ReadValue(image.fileBytes, directoriesOffset + 8, importRva);
        ReadValue(image.fileBytes, directoriesOffset + 12, importSize);
        ReadValue(image.fileBytes, directoriesOffset + 16, resourceRva);
        ReadValue(image.fileBytes, directoriesOffset + 20, resourceSize);
        if (!ParseImports(image, importRva, importSize, is64Bit,
                          image.diagnostic) ||
            !DetectManifest(image, resourceRva, resourceSize,
                            image.hasEmbeddedManifest, image.diagnostic)) {
            return image;
        }

        image.valid = true;
        image.diagnostic = L"Valid bounded PE image.";
    } catch (const std::exception& error) {
        image.valid = false;
        image.diagnostic = L"PE inspection failed: " + Utf8ToWide(error.what());
    } catch (...) {
        image.valid = false;
        image.diagnostic = L"PE inspection failed with an unknown error.";
    }
    return image;
}

}  // namespace blind_soldier
