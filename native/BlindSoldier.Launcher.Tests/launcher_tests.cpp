#define BLIND_SOLDIER_NATIVE_TESTS
#include "../BlindSoldier.Launcher/launcher.cpp"

#include <atomic>
#include <cstdlib>
#include <memory>
#include <thread>

[[noreturn]] static void CheckFailed(const wchar_t* expression,
                                     const char* file, int line) {
    fwprintf(stderr, L"CHECK failed at %hs:%d: %ls\n", file, line,
             expression);
    ExitProcess(100);
}

#define CHECK(expression) \
    do { if (!(expression)) CheckFailed(L#expression, __FILE__, __LINE__); } while (0)

static fs::path NewTestRoot(const wchar_t* suffix) {
    fs::path root = fs::temp_directory_path() /
        (L"blind-soldier-launcher-tests-" +
         std::to_wstring(GetCurrentProcessId()) + L"-" + suffix);
    std::error_code error;
    fs::remove_all(root, error);
    fs::create_directories(root);
    return root;
}

static std::string ReadRequired(const fs::path& path) {
    std::string value;
    CHECK(ReadUtf8File(path, value));
    return value;
}

static fs::path AtomicTemporaryPath(const fs::path& path) {
    fs::path result = path;
    result += L".blind_soldier." + std::to_wstring(GetCurrentProcessId()) +
              L"." + std::to_wstring(GetCurrentThreadId()) + L".tmp";
    return result;
}

int wmain(int argumentCount, wchar_t** arguments) {
    if (argumentCount > 1 &&
        wcscmp(arguments[1], L"--prove-check-failure") == 0) {
        CHECK(false);
    }
    fwprintf(stderr, L"restore\n");
    {
        fs::path root = NewTestRoot(L"restore");
        fs::path pointer = root / L"ReloadedII.json";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        Logger log;
        log.Open(root, L"restore.log");
        {
            AppDataSwap swap(root / L"portable", log, pointer);
            CHECK(swap.Ready());
            CHECK(ReadRequired(pointer) != "original");
            CHECK(fs::exists(pointer.wstring() + L".blind_soldier_backup"));
        }
        CHECK(ReadRequired(pointer) == "original");
        CHECK(!fs::exists(pointer.wstring() + L".blind_soldier_backup"));
        log.Close();
        fs::remove_all(root);
    }

    fwprintf(stderr, L"backup-failure\n");
    {
        fs::path root = NewTestRoot(L"backup-failure");
        fs::path pointer = root / L"ReloadedII.json";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        HANDLE lock = CreateFileW(pointer.c_str(), GENERIC_READ, 0, nullptr,
                                  OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        CHECK(lock != INVALID_HANDLE_VALUE);
        Logger log;
        log.Open(root, L"backup-failure.log");
        AppDataSwap swap(root / L"portable", log, pointer);
        CHECK(!swap.Ready());
        CloseHandle(lock);
        CHECK(ReadRequired(pointer) == "original");
        log.Close();
        fs::remove_all(root);
    }

    fwprintf(stderr, L"write-failure\n");
    {
        fs::path root = NewTestRoot(L"write-failure");
        fs::path pointer = root / L"ReloadedII.json";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        fs::create_directory(AtomicTemporaryPath(pointer));
        Logger log;
        log.Open(root, L"write-failure.log");
        AppDataSwap swap(root / L"portable", log, pointer);
        CHECK(!swap.Ready());
        CHECK(ReadRequired(pointer) == "original");
        CHECK(!fs::exists(pointer.wstring() + L".blind_soldier_backup"));
        log.Close();
        fs::remove_all(root);
    }

    fwprintf(stderr, L"restore-failure\n");
    {
        fs::path root = NewTestRoot(L"restore-failure");
        fs::path pointer = root / L"ReloadedII.json";
        fs::path backup = pointer.wstring() + L".blind_soldier_backup";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        Logger log;
        log.Open(root, L"restore-failure.log");
        auto swap = std::make_unique<AppDataSwap>(root / L"portable", log,
                                                  pointer);
        CHECK(swap->Ready());
        HANDLE lock = CreateFileW(backup.c_str(), GENERIC_READ, 0, nullptr,
                                  OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        CHECK(lock != INVALID_HANDLE_VALUE);
        swap.reset();
        CloseHandle(lock);
        CHECK(fs::exists(backup));
        CHECK(ReadRequired(backup) == "original");
        CHECK(ReadRequired(pointer) != "original");
        {
            AppDataSwap recovery(root / L"portable", log, pointer);
            CHECK(recovery.Ready());
        }
        CHECK(ReadRequired(pointer) == "original");
        CHECK(!fs::exists(backup));
        log.Close();
        fs::remove_all(root);
    }

    fwprintf(stderr, L"external-change\n");
    {
        fs::path root = NewTestRoot(L"external-change");
        fs::path pointer = root / L"ReloadedII.json";
        fs::path backup = pointer.wstring() + L".blind_soldier_backup";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        Logger log;
        log.Open(root, L"external-change.log");
        {
            AppDataSwap swap(root / L"portable", log, pointer);
            CHECK(swap.Ready());
            CHECK(WriteUtf8FileAtomic(pointer, L"external"));
        }
        CHECK(ReadRequired(pointer) == "external");
        CHECK(ReadRequired(backup) == "original");
        {
            AppDataSwap retry(root / L"portable", log, pointer);
            CHECK(!retry.Ready());
        }
        CHECK(ReadRequired(pointer) == "external");
        CHECK(ReadRequired(backup) == "original");
        log.Close();
        fs::remove_all(root);
    }

    fwprintf(stderr, L"concurrent\n");
    {
        fs::path root = NewTestRoot(L"concurrent");
        fs::path pointer = root / L"ReloadedII.json";
        CHECK(WriteUtf8FileAtomic(pointer, L"original"));
        Logger firstLog;
        firstLog.Open(root, L"first.log");
        auto first = std::make_unique<AppDataSwap>(root / L"portable",
                                                   firstLog, pointer);
        CHECK(first->Ready());
        std::atomic<bool> entered = false;
        std::atomic<bool> completed = false;
        std::thread second([&]() {
            entered = true;
            Logger secondLog;
            secondLog.Open(root, L"second.log");
            AppDataSwap swap(root / L"portable", secondLog, pointer);
            CHECK(swap.Ready());
            completed = true;
            secondLog.Close();
        });
        while (!entered) Sleep(1);
        Sleep(100);
        CHECK(!completed);
        first.reset();
        fwprintf(stderr, L"first released\n");
        second.join();
        fwprintf(stderr, L"second joined\n");
        CHECK(completed);
        CHECK(ReadRequired(pointer) == "original");
        fwprintf(stderr, L"pointer restored\n");
        firstLog.Close();
        fs::remove_all(root);
        fwprintf(stderr, L"concurrent cleaned\n");
    }

    fwprintf(stderr, L"done\n");
    return 0;
}
