#include "bootstrap_contract.h"

#include <limits>
#include <set>

namespace blind_soldier {
namespace {

bool IsHex(wchar_t value) {
    return (value >= L'0' && value <= L'9') ||
           (value >= L'a' && value <= L'f') ||
           (value >= L'A' && value <= L'F');
}

bool IsCanonicalGuid(const std::wstring& value) {
    if (value.size() != 36) return false;
    for (size_t index = 0; index < value.size(); ++index) {
        bool hyphen = index == 8 || index == 13 || index == 18 || index == 23;
        if (hyphen ? value[index] != L'-' : !IsHex(value[index])) return false;
    }
    return true;
}

bool IsReadyEventName(const std::wstring& value) {
    static constexpr wchar_t prefix[] = L"Local\\BlindSoldier.Ready.";
    if (value.size() <= wcslen(prefix) || value.size() > 240 ||
        value.compare(0, wcslen(prefix), prefix) != 0) {
        return false;
    }
    for (size_t index = wcslen(prefix); index < value.size(); ++index) {
        wchar_t character = value[index];
        if (!iswalnum(character) && character != L'-' &&
            character != L'_' && character != L'.') {
            return false;
        }
    }
    return true;
}

bool ReadPid(const std::wstring& value, DWORD& result) {
    if (value.empty() || value.front() == L'+' || value.front() == L'-')
        return false;
    wchar_t* end = nullptr;
    errno = 0;
    unsigned long long parsed = wcstoull(value.c_str(), &end, 10);
    if (errno != 0 || end != value.c_str() + value.size() || parsed == 0 ||
        parsed > std::numeric_limits<DWORD>::max()) {
        return false;
    }
    result = static_cast<DWORD>(parsed);
    return true;
}

}  // namespace

bool TryParseBootstrapRequest(
    const std::vector<std::wstring>& arguments,
    BootstrapRequest& request,
    std::wstring& error) {
    request = {};
    error.clear();
    bool launch = false;
    bool attach = false;
    std::set<std::wstring> seen;
    std::wstring pid;

    for (size_t index = 0; index < arguments.size();) {
        const std::wstring& option = arguments[index++];
        if (option != L"--launch" && option != L"--attach" &&
            option != L"--root" && option != L"--game" &&
            option != L"--pid" && option != L"--game-arguments" &&
            option != L"--ready-event" && option != L"--launch-id") {
            error = L"Unknown bootstrap option: " + option;
            return false;
        }
        if (!seen.insert(option).second) {
            error = L"Duplicate bootstrap option: " + option;
            return false;
        }
        if (option == L"--launch") {
            launch = true;
            continue;
        }
        if (option == L"--attach") {
            attach = true;
            continue;
        }
        if (index >= arguments.size() ||
            arguments[index].rfind(L"--", 0) == 0) {
            error = L"Bootstrap option has no value: " + option;
            return false;
        }
        const std::wstring& value = arguments[index++];
        if (option == L"--root") request.packageRoot = value;
        else if (option == L"--game") request.gameExecutable = value;
        else if (option == L"--pid") pid = value;
        else if (option == L"--game-arguments") request.gameArguments = value;
        else if (option == L"--ready-event") request.readyEventName = value;
        else if (option == L"--launch-id") request.launchId = value;
    }

    if (launch == attach) {
        error = L"Specify exactly one of --launch or --attach.";
        return false;
    }
    request.mode = launch ? BootstrapMode::Launch : BootstrapMode::Attach;
    if (request.packageRoot.empty() || request.gameExecutable.empty() ||
        request.launchId.empty()) {
        error = L"--root, --game, and --launch-id are required.";
        return false;
    }
    if (!request.packageRoot.is_absolute() ||
        !request.gameExecutable.is_absolute()) {
        error = L"--root and --game must be absolute paths.";
        return false;
    }
    if (!IsCanonicalGuid(request.launchId)) {
        error = L"--launch-id must be a canonical GUID without braces.";
        return false;
    }
    if (!request.gameArguments.empty() && request.gameArguments != L"jp") {
        error = L"--game-arguments accepts only the supported value jp.";
        return false;
    }
    if (request.mode == BootstrapMode::Launch) {
        if (!pid.empty() || !request.readyEventName.empty()) {
            error = L"Launch mode does not accept --pid or --ready-event.";
            return false;
        }
    } else {
        if (!request.gameArguments.empty() ||
            !ReadPid(pid, request.processId) ||
            !IsReadyEventName(request.readyEventName)) {
            error = L"Attach mode requires a valid --pid and Local Blind Soldier ready event.";
            return false;
        }
    }
    return true;
}

}  // namespace blind_soldier
