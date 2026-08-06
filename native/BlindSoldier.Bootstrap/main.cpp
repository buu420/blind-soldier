#include "process_bootstrap.h"

using namespace blind_soldier;

namespace {

#ifdef _WIN64
constexpr const wchar_t* kArchitecture = L"x64";
#else
constexpr const wchar_t* kArchitecture = L"x86";
#endif

std::wstring LogName(const std::wstring& launchId) {
    return L"Blind-Soldier-Bootstrap-" + std::wstring(kArchitecture) + L"-" +
           (launchId.empty() ? L"invalid-command" : launchId) + L".log";
}

}  // namespace

#ifndef BLIND_SOLDIER_NATIVE_TESTS
int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    int argumentCount = 0;
    LPWSTR* raw = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    std::vector<std::wstring> arguments;
    if (raw) {
        for (int index = 1; index < argumentCount; ++index)
            arguments.emplace_back(raw[index]);
        LocalFree(raw);
    }

    BootstrapRequest request{};
    std::wstring error;
    bool parsed = raw && TryParseBootstrapRequest(arguments, request, error);
    fs::path logRoot = parsed
        ? request.packageRoot / L"Blind-Soldier" / L"Logs"
        : SelfDir();
    Logger log;
    std::wstring logName = LogName(parsed ? request.launchId : L"");
    log.Open(logRoot, logName.c_str());
    log.A("=== Blind Soldier portable bootstrap start ===");
    log.W(L"commandLine=" + std::wstring(GetCommandLineW()));
    if (!parsed) {
        log.W(L"Argument validation failed: " + error);
        log.Close();
        return static_cast<int>(BootstrapExitCode::InvalidArguments);
    }
    BootstrapExitCode result = RunBootstrap(request, log);
    log.W(L"Bootstrap exit code=" +
          std::to_wstring(static_cast<int>(result)));
    log.Close();
    return static_cast<int>(result);
}
#endif
