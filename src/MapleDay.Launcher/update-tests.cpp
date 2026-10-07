#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <iostream>
#include "update.h"
static void bytes(const std::filesystem::path& file, const char* data) {
    std::filesystem::create_directories(file.parent_path());
    std::ofstream output(file, std::ios::binary); output << data;
}
static void require(bool condition) { if (!condition) throw std::runtime_error("test assertion"); }
int wmain(int argc, wchar_t** argv) {
    if (argc == 1) {
        // Fake parent/restart target for process tests. Never opens a UI or WinUI.
        wchar_t executable[32768]{};
        if (!GetModuleFileNameW(nullptr, executable, 32768)) return 2;
        const auto directory = std::filesystem::path(executable).parent_path();
        if (directory.filename() == L"App") Sleep(1000);
        else bytes(directory / L"restarted.txt", "restarted");
        return 0;
    }
    if (argc != 2) return 1;
    const auto root = std::filesystem::path(argv[1]);
    const auto job = root / L"App" / L".update-test-job";
    try {
        std::filesystem::create_directories(root / L"App");
        bytes(root / L"MapleDay.exe", "unchanged launcher");
        const auto stamp = std::filesystem::last_write_time(root / L"MapleDay.exe");
        bytes(root / L"App" / L"first.dll", "old first");
        bytes(root / L"App" / L"obsolete.dll", "remove me");
        bytes(job / L"files" / L"App" / L"first.dll", "new first");
        bytes(job / L"files" / L"App" / L"added.dll", "new added");
        bytes(root / L"App" / L"added.dll.missing", "old sibling");
        const auto siblingHash = updates::hash(root / L"App" / L"added.dll.missing");
        bytes(job / L"files" / L"App" / L"added.dll.missing", "new sibling");
        auto operations = std::vector<updates::operation>{
            {L'R', L"App/first.dll", updates::hash(job / L"files" / L"App" / L"first.dll"), 9},
            {L'R', L"App/added.dll", updates::hash(job / L"files" / L"App" / L"added.dll"), 9},
            {L'R', L"App/added.dll.missing", updates::hash(job / L"files" / L"App" / L"added.dll.missing"), 11},
            {L'D', L"App/obsolete.dll", L"-", 0}};
        const auto oldHash = updates::hash(root / L"App" / L"first.dll");
        updates::apply(root, job, operations);
        require(updates::hash(root / L"App" / L"first.dll") == operations[0].hash);
        require(std::filesystem::exists(root / L"App" / L"added.dll"));
        require(!std::filesystem::exists(root / L"App" / L"obsolete.dll"));
        require(std::filesystem::last_write_time(root / L"MapleDay.exe") == stamp);
        updates::rollback(root, job, operations);
        require(updates::hash(root / L"App" / L"first.dll") == oldHash);
        require(!std::filesystem::exists(root / L"App" / L"added.dll"));
        require(updates::hash(root / L"App" / L"added.dll.missing") == siblingHash);
        require(std::filesystem::exists(root / L"App" / L"obsolete.dll"));
        std::filesystem::remove_all(job / L"backup");
        std::filesystem::remove_all(job / L"missing");
        std::filesystem::remove(job / L"committed");
        // Hold the third operation locked so the first two must be rolled back.
        HANDLE lock = CreateFileW((root / L"App" / L"obsolete.dll").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        require(lock != INVALID_HANDLE_VALUE);
        bool failed = false;
        try { updates::apply(root, job, operations); } catch (...) { failed = true; }
        CloseHandle(lock);
        require(failed);
        require(updates::hash(root / L"App" / L"first.dll") == oldHash);
        require(!std::filesystem::exists(root / L"App" / L"added.dll"));
        require(!std::filesystem::exists(root / L"App" / L".update-job"));
        require(std::filesystem::last_write_time(root / L"MapleDay.exe") == stamp);
        operations[0].hash = std::wstring(64, L'0');
        failed = false; try { updates::apply(root, job, operations); } catch (...) { failed = true; }
        require(failed && updates::hash(root / L"App" / L"first.dll") == oldHash);
        require(!updates::safe(L"App/../outside.dll") && !updates::safe(L"App/Uninstall/unins000.exe") && !updates::safe(L"App/CON.dll"));
        // Exercise the complete process protocol: staged verification, readiness,
        // natural parent exit, replacement and restart using console-free stubs.
        std::filesystem::remove_all(job);
        std::filesystem::create_directories(job / L"files" / L"App");
        wchar_t executable[32768]{};
        require(GetModuleFileNameW(nullptr, executable, 32768) != 0);
        require(CopyFileW(executable, (root / L"MapleDay.exe").c_str(), FALSE));
        require(CopyFileW(executable, (root / L"App" / L"MapleDay.exe").c_str(), FALSE));
        bytes(job / L"files" / L"App" / L"first.dll", "restart test");
        updates::write(job / L"root.txt", root.wstring());
        updates::write(job / L"plan.txt", L"R\tApp/first.dll\t" + updates::hash(job / L"files" / L"App" / L"first.dll") + L"\t12\n");
        const auto parent = root / L"App" / L"MapleDay.exe";
        auto command = L"\"" + parent.wstring() + L"\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        require(CreateProcessW(parent.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process));
        const int result = updates::worker(job, process.dwProcessId, false);
        CloseHandle(process.hThread); CloseHandle(process.hProcess);
        require(result == 0 && updates::read(job / L"status.txt") == L"ready");
        for (int attempt = 0; attempt < 100 && !std::filesystem::exists(root / L"restarted.txt"); ++attempt) Sleep(20);
        require(std::filesystem::exists(root / L"restarted.txt"));
        Sleep(100); // The console-free restart stub has finished writing.
        std::filesystem::remove_all(root);
        std::cout << "Native update: changed-only replacement, removal, locked-file rollback, unchanged timestamps, integrity and process exit/restart passed without UI.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
