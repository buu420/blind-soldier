#include "app_loader_readiness.h"

#include <cstddef>
#include <string_view>

namespace blind_soldier {
namespace {

enum class AppLoaderRecordKind { Init, Success };

struct AppLoaderRecord {
    AppLoaderRecordKind kind;
    FILETIME timestamp{};
};

bool IsDigit(char character) {
    return character >= '0' && character <= '9';
}

unsigned int ParseNumber(const char* text, size_t count) {
    unsigned int value = 0;
    for (size_t index = 0; index < count; ++index) {
        value = value * 10U + static_cast<unsigned int>(text[index] - '0');
    }
    return value;
}

bool LocalSystemTimeToFileTime(const SYSTEMTIME& local, FILETIME& fileTime) {
    SYSTEMTIME utc{};
    using ConvertEx = BOOL(WINAPI*)(const DYNAMIC_TIME_ZONE_INFORMATION*,
                                    const SYSTEMTIME*, LPSYSTEMTIME);
    const HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
    const auto convertEx = kernel == nullptr ? nullptr :
        reinterpret_cast<ConvertEx>(GetProcAddress(
            kernel, "TzSpecificLocalTimeToSystemTimeEx"));
    const bool converted = convertEx != nullptr ?
        convertEx(nullptr, &local, &utc) != FALSE :
        TzSpecificLocalTimeToSystemTime(nullptr, &local, &utc) != FALSE;
    return converted && SystemTimeToFileTime(&utc, &fileTime) != FALSE;
}

bool ParseRecord(const char* line, size_t length, AppLoaderRecord& record) {
    constexpr size_t kTimestampLength = 23;
    constexpr std::string_view kInitSuffix = " INFO  AppLoader init log";
    constexpr std::string_view kSuccessSuffix =
        " INFO  AppLoader started successfully";
    if (length < kTimestampLength || line[4] != '-' || line[7] != '-' ||
        line[10] != ' ' || line[13] != ':' || line[16] != ':' ||
        line[19] != '.') {
        return false;
    }
    for (const size_t index : {0U, 1U, 2U, 3U, 5U, 6U, 8U, 9U, 11U, 12U,
                               14U, 15U, 17U, 18U, 20U, 21U, 22U}) {
        if (!IsDigit(line[index])) return false;
    }

    const std::string_view suffix(line + kTimestampLength,
                                  length - kTimestampLength);
    if (suffix == kInitSuffix) {
        record.kind = AppLoaderRecordKind::Init;
    } else if (suffix == kSuccessSuffix) {
        record.kind = AppLoaderRecordKind::Success;
    } else {
        return false;
    }

    SYSTEMTIME local{};
    local.wYear = static_cast<WORD>(ParseNumber(line, 4));
    local.wMonth = static_cast<WORD>(ParseNumber(line + 5, 2));
    local.wDay = static_cast<WORD>(ParseNumber(line + 8, 2));
    local.wHour = static_cast<WORD>(ParseNumber(line + 11, 2));
    local.wMinute = static_cast<WORD>(ParseNumber(line + 14, 2));
    local.wSecond = static_cast<WORD>(ParseNumber(line + 17, 2));
    local.wMilliseconds = static_cast<WORD>(ParseNumber(line + 20, 3));
    return LocalSystemTimeToFileTime(local, record.timestamp);
}

AppLoaderGateDecision Decision(AppLoaderGateState state, bool ready,
                               bool seventhHeaven,
                               const wchar_t* diagnostic = L"") {
    AppLoaderGateDecision result;
    result.state = state;
    result.ready = ready;
    result.seventhHeaven = seventhHeaven;
    result.diagnostic = diagnostic;
    return result;
}

}  // namespace

AppLoaderReadinessTracker::AppLoaderReadinessTracker(
    ULONGLONG directDiscoveryMilliseconds, ULONGLONG timeoutMilliseconds)
    : directDiscoveryMilliseconds_(directDiscoveryMilliseconds),
      timeoutMilliseconds_(timeoutMilliseconds) {}

AppLoaderGateDecision AppLoaderReadinessTracker::Observe(
    const AppLoaderObservation& observation) {
    seventhHeaven_ = seventhHeaven_ ||
        observation.hostKind == SupportedHostKind::SevenHeavenX86 ||
        observation.stockLoaderSignaturePresent ||
        observation.recognizedFfnxModulePresent;

    if (!observation.processAlive) {
        return Decision(AppLoaderGateState::Failed, false, seventhHeaven_,
                        L"The Final Fantasy VII process exited before readiness.");
    }
    if (observation.elapsedMilliseconds >= timeoutMilliseconds_) {
        return Decision(AppLoaderGateState::Failed, false, seventhHeaven_,
                        L"Timed out waiting for AppLoader readiness.");
    }
    if (!seventhHeaven_) {
        if (observation.elapsedMilliseconds < directDiscoveryMilliseconds_) {
            return Decision(AppLoaderGateState::Discovering, false, false);
        }
        return Decision(AppLoaderGateState::ReadyDirect, true, false);
    }

    bool sawCurrentInit = false;
    bool sawCurrentSuccess = false;
    size_t start = 0;
    while (start < observation.appLoaderLog.size()) {
        size_t end = observation.appLoaderLog.find('\n', start);
        if (end == std::string::npos) end = observation.appLoaderLog.size();
        size_t length = end - start;
        if (length != 0 && observation.appLoaderLog[start + length - 1] == '\r') {
            --length;
        }
        AppLoaderRecord record{};
        if (ParseRecord(observation.appLoaderLog.data() + start, length, record) &&
            CompareFileTime(&record.timestamp, &observation.processCreation) >= 0) {
            if (record.kind == AppLoaderRecordKind::Init) {
                sawCurrentInit = true;
                sawCurrentSuccess = false;
            } else if (sawCurrentInit) {
                sawCurrentSuccess = true;
            }
        }
        if (end == observation.appLoaderLog.size()) break;
        start = end + 1;
    }

    if (!sawCurrentInit) {
        return Decision(AppLoaderGateState::WaitingForCurrentLog, false, true);
    }
    if (!sawCurrentSuccess) {
        return Decision(AppLoaderGateState::WaitingForSuccess, false, true);
    }
    if (observation.wrapperProfilePresent) {
        return Decision(AppLoaderGateState::WaitingForProfileConsumption,
                        false, true);
    }
    return Decision(AppLoaderGateState::ReadySeventhHeaven, true, true);
}

}  // namespace blind_soldier
