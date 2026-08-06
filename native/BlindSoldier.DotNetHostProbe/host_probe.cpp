#include <windows.h>

#include <filesystem>
#include <iostream>
#include <string>

#include <nethost.h>

namespace fs = std::filesystem;

namespace {

bool ClearEnvironment(const wchar_t* name) {
    return SetEnvironmentVariableW(name, nullptr) != FALSE ||
        GetLastError() == ERROR_ENVVAR_NOT_FOUND;
}

std::wstring CanonicalPath(const fs::path& path) {
    std::error_code error;
    const fs::path result = fs::weakly_canonical(path, error);
    if (error) {
        return {};
    }
    std::wstring value = result.native();
    while (value.size() > 3 &&
           (value.back() == L'\\' || value.back() == L'/')) {
        value.pop_back();
    }
    return value;
}

bool EqualPath(const fs::path& left, const fs::path& right) {
    const std::wstring leftValue = CanonicalPath(left);
    const std::wstring rightValue = CanonicalPath(right);
    return !leftValue.empty() && !rightValue.empty() &&
        _wcsicmp(leftValue.c_str(), rightValue.c_str()) == 0;
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) {
        std::wcerr << L"Usage: BlindSoldier.DotNetHostProbe.exe "
                      L"<runtime-root> <expected-hostfxr>\n";
        return 10;
    }

    const fs::path runtimeRoot = argv[1];
    const fs::path expectedHostFxr = argv[2];
    if (!fs::is_directory(runtimeRoot) || !fs::is_regular_file(expectedHostFxr)) {
        std::wcerr << L"The private runtime fixture is incomplete.\n";
        return 11;
    }

    // Remove every inherited .NET root before setting the single
    // architecture-specific private root. This prevents a machine-wide SDK or
    // runtime from making the proof pass accidentally.
    for (const wchar_t* name : {
             L"DOTNET_ROOT", L"DOTNET_ROOT_X64", L"DOTNET_ROOT_X86",
             L"DOTNET_ROOT(x86)"}) {
        if (!ClearEnvironment(name)) {
            std::wcerr << L"Could not clear inherited environment variable: "
                       << name << L"\n";
            return 12;
        }
    }

#if defined(_WIN64)
    constexpr const wchar_t* privateRootName = L"DOTNET_ROOT_X64";
#else
    constexpr const wchar_t* privateRootName = L"DOTNET_ROOT_X86";
#endif
    if (!SetEnvironmentVariableW(privateRootName,
                                 runtimeRoot.c_str())) {
        std::wcerr << L"Could not set the private runtime root.\n";
        return 13;
    }

    size_t bufferSize = 32768;
    std::wstring hostFxr(bufferSize, L'\0');
    const int result = get_hostfxr_path(hostFxr.data(), &bufferSize, nullptr);
    if (result != 0) {
        std::wcerr << L"get_hostfxr_path failed: 0x" << std::hex
                   << static_cast<unsigned int>(result) << L"\n";
        return 20;
    }
    hostFxr.resize(wcslen(hostFxr.c_str()));

    if (!EqualPath(hostFxr, expectedHostFxr)) {
        std::wcerr << L"nethost resolved an unexpected hostfxr.\nExpected: "
                   << expectedHostFxr.native() << L"\nActual:   "
                   << hostFxr << L"\n";
        return 21;
    }

    std::wcout << L"private hostfxr resolved: " << hostFxr << L"\n";
    return 0;
}
