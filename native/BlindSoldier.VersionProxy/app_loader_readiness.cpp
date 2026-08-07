#include "app_loader_readiness.h"

#include <array>
#include <cstddef>
#include <string_view>
#include <vector>

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


constexpr DWORD kMaximumAppLoaderLogBytes = 4U * 1024U * 1024U;

bool CanonicalizePath(const fs::path& path, fs::path& canonical) {
    std::error_code error;
    canonical = fs::weakly_canonical(path, error);
    return !error && !canonical.empty() && canonical.is_absolute();
}


fs::path AbsoluteProcessImagePath(const fs::path& processImage) {
    std::error_code error;
    fs::path absolute = fs::absolute(processImage, error);
    if (!error && !absolute.empty() && absolute.is_absolute()) {
        return absolute.lexically_normal();
    }

    std::array<wchar_t, 32768> currentImage{};
    const DWORD length = GetModuleFileNameW(
        nullptr, currentImage.data(), static_cast<DWORD>(currentImage.size()));
    if (length != 0 && length < currentImage.size()) {
        fs::path current(currentImage.data(), currentImage.data() + length);
        if (current.is_absolute()) return current.lexically_normal();
    }
    return fs::path(L"C:\\") /
        (processImage.filename().empty() ? L"ff7_en.exe"
                                         : processImage.filename());
}

bool IsOrdinaryFile(const fs::path& path) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
        (attributes & (FILE_ATTRIBUTE_DIRECTORY |
                       FILE_ATTRIBUTE_REPARSE_POINT)) == 0;
}

bool LoadedModuleMatches(const wchar_t* moduleName,
                         const fs::path& expectedPath) {
    const HMODULE module = GetModuleHandleW(moduleName);
    if (!module) return false;
    std::array<wchar_t, 32768> buffer{};
    const DWORD length = GetModuleFileNameW(
        module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return false;
    fs::path canonicalModule;
    fs::path canonicalExpected;
    return CanonicalizePath(fs::path(buffer.data(), buffer.data() + length),
                            canonicalModule) &&
        CanonicalizePath(expectedPath, canonicalExpected) &&
        _wcsicmp(canonicalModule.c_str(), canonicalExpected.c_str()) == 0;
}

bool HasStockLoaderSignature(const fs::path& gameDirectory) {
    if (!LoadedModuleMatches(L"dinput.dll", gameDirectory / L"dinput.dll")) {
        return false;
    }
    for (const wchar_t* name : {L"AppProxy.runtimeconfig.json",
                                L"AppProxy.dll", L"AppWrapper.dll",
                                L"nethost.dll"}) {
        if (!IsOrdinaryFile(gameDirectory / name)) return false;
    }
    return true;
}

bool HasRecognizedFfnxModule() {
    for (const wchar_t* name : {L"AF3DN.P", L"7H_GameDriver.dll",
                                L"FFNx.dll"}) {
        if (GetModuleHandleW(name)) return true;
    }
    return false;
}

std::string ReadAppLoaderLogTail(const fs::path& path) {
    HANDLE file = CreateFileW(
        path.c_str(), GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0) {
        CloseHandle(file);
        return {};
    }
    const ULONGLONG byteCount = static_cast<ULONGLONG>(size.QuadPart) >
        kMaximumAppLoaderLogBytes
        ? kMaximumAppLoaderLogBytes
        : static_cast<ULONGLONG>(size.QuadPart);
    LARGE_INTEGER offset{};
    offset.QuadPart = size.QuadPart - static_cast<LONGLONG>(byteCount);
    if (!SetFilePointerEx(file, offset, nullptr, FILE_BEGIN)) {
        CloseHandle(file);
        return {};
    }

    std::vector<char> bytes(static_cast<size_t>(byteCount));
    DWORD read = 0;
    const BOOL readOk = ReadFile(file, bytes.data(),
        static_cast<DWORD>(bytes.size()), &read, nullptr);
    CloseHandle(file);
    if (!readOk) return {};

    std::string text(bytes.data(), bytes.data() + read);
    if (offset.QuadPart != 0) {
        const size_t firstNewline = text.find('\n');
        if (firstNewline == std::string::npos) return {};
        text.erase(0, firstNewline + 1);
    }
    constexpr std::string_view initMarker = " INFO  AppLoader init log";
    const size_t lastInit = text.rfind(initMarker);
    if (lastInit != std::string::npos) {
        const size_t lineStart = text.rfind('\n', lastInit);
        text.erase(0, lineStart == std::string::npos ? 0 : lineStart + 1);
    }
    return text;
}

bool CurrentProcessAlive() {
    DWORD exitCode = 0;
    return GetExitCodeProcess(GetCurrentProcess(), &exitCode) != FALSE &&
        exitCode == STILL_ACTIVE;
}

const wchar_t* GateStateName(AppLoaderGateState state) {
    switch (state) {
        case AppLoaderGateState::Discovering: return L"discovering";
        case AppLoaderGateState::WaitingForCurrentLog:
            return L"waiting-for-current-log";
        case AppLoaderGateState::WaitingForSuccess:
            return L"waiting-for-success";
        case AppLoaderGateState::WaitingForProfileConsumption:
            return L"waiting-for-profile-consumption";
        case AppLoaderGateState::ReadyDirect: return L"ready-direct";
        case AppLoaderGateState::ReadySeventhHeaven:
            return L"ready-seventh-heaven";
        case AppLoaderGateState::Failed: return L"failed";
    }
    return L"unknown";
}

}  // namespace

AppLoaderReadinessTracker::AppLoaderReadinessTracker(
    ULONGLONG directDiscoveryMilliseconds, ULONGLONG timeoutMilliseconds)
    : directDiscoveryMilliseconds_(directDiscoveryMilliseconds),
      timeoutMilliseconds_(timeoutMilliseconds) {}

AppLoaderGateDecision AppLoaderReadinessTracker::Observe(
    const AppLoaderObservation& observation) {
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

    seventhHeaven_ = seventhHeaven_ ||
        observation.stockLoaderSignaturePresent ||
        observation.wrapperProfilePresent || sawCurrentInit;

    if (!observation.processAlive) {
        return Decision(AppLoaderGateState::Failed, false, seventhHeaven_,
                        L"The Final Fantasy VII process exited before readiness.");
    }
    if (observation.elapsedMilliseconds >= timeoutMilliseconds_) {
        return Decision(AppLoaderGateState::Failed, false, seventhHeaven_,
                        L"Timed out waiting for AppLoader readiness.");
    }
    if (!seventhHeaven_) {
        if (observation.hostKind != SupportedHostKind::LegacyStockX86 &&
            observation.hostKind != SupportedHostKind::SevenHeavenX86) {
            return Decision(AppLoaderGateState::Failed, false, false,
                            L"Unsupported host cannot use direct readiness.");
        }
        if (observation.elapsedMilliseconds < directDiscoveryMilliseconds_) {
            return Decision(AppLoaderGateState::Discovering, false, false);
        }
        return Decision(AppLoaderGateState::ReadyDirect, true, false);
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


StockRuntimeReadinessResult WaitForStockRuntimeReadiness(
    const fs::path& processImage,
    const HostValidationResult& host,
    Logger& log,
    DWORD pollMilliseconds,
    ULONGLONG timeoutMilliseconds) {
    StockRuntimeReadinessResult result;
    const fs::path absoluteImage = AbsoluteProcessImagePath(processImage);
    fs::path appLoaderLog =
        absoluteImage.parent_path() / L"AppLoader.log";
    fs::path canonicalImage;
    if (!CanonicalizePath(processImage, canonicalImage)) {
        result.diagnostic =
            L"The FFVII executable path could not be resolved for AppLoader readiness. AppLoader.log: " +
            appLoaderLog.wstring();
        return result;
    }
    const fs::path gameDirectory = canonicalImage.parent_path();
    appLoaderLog = gameDirectory / L"AppLoader.log";
    const fs::path wrapperProfile = gameDirectory / L".7thWrapperProfile";

    FILETIME processCreation{};
    FILETIME exitTime{};
    FILETIME kernelTime{};
    FILETIME userTime{};
    if (!GetProcessTimes(GetCurrentProcess(), &processCreation, &exitTime,
                         &kernelTime, &userTime)) {
        result.diagnostic = L"The FFVII process creation time could not be read. "
            L"AppLoader.log: " + appLoaderLog.wstring();
        return result;
    }

    AppLoaderReadinessTracker tracker(3000, timeoutMilliseconds);
    const ULONGLONG started = GetTickCount64();
    bool loggedState = false;
    AppLoaderGateState previousState = AppLoaderGateState::Discovering;
    for (;;) {
        AppLoaderObservation observation;
        observation.hostKind = host.kind;
        observation.stockLoaderSignaturePresent =
            HasStockLoaderSignature(gameDirectory);
        observation.recognizedFfnxModulePresent = HasRecognizedFfnxModule();
        observation.processAlive = CurrentProcessAlive();
        observation.elapsedMilliseconds = GetTickCount64() - started;
        observation.appLoaderLog = ReadAppLoaderLogTail(appLoaderLog);
        observation.processCreation = processCreation;
        observation.wrapperProfilePresent =
            GetFileAttributesW(wrapperProfile.c_str()) != INVALID_FILE_ATTRIBUTES;

        const AppLoaderGateDecision decision = tracker.Observe(observation);
        if (!loggedState || decision.state != previousState) {
            log.W(L"AppLoader readiness state=" +
                  std::wstring(GateStateName(decision.state)));
            previousState = decision.state;
            loggedState = true;
        }
        if (decision.ready) {
            result.ready = true;
            result.seventhHeaven = decision.seventhHeaven;
            return result;
        }
        if (decision.state == AppLoaderGateState::Failed) {
            result.seventhHeaven = decision.seventhHeaven;
            result.diagnostic = decision.diagnostic.empty()
                ? L"AppLoader readiness failed. AppLoader.log: " +
                    appLoaderLog.wstring()
                : decision.diagnostic + L" AppLoader.log: " +
                    appLoaderLog.wstring();
            return result;
        }
        Sleep(pollMilliseconds);
    }
}


}  // namespace blind_soldier
