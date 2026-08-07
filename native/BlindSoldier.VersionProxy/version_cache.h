#pragma once

#include "../BlindSoldier.Common/common.h"

namespace blind_soldier {

bool BuildCachedSystemVersion(const fs::path& source,
                              const fs::path& cacheDirectory,
                              fs::path& cached);

bool BuildCachedSystemVersion(const fs::path& source,
                              fs::path& cached);

}  // namespace blind_soldier
