#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>

// Only Windows system DLLs are needed by this launcher; WinUI/.NET live in App.
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int)
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return 2;
    const std::wstring executable(buffer.data(), length);
    const auto separator = executable.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return 2;
    const auto appDirectory = executable.substr(0, separator) + L"\\App";
    const auto appPath = appDirectory + L"\\MapleDay.exe";
    const std::wstring forwarded = arguments ? arguments : L"";
    const auto first = forwarded.find_first_not_of(L" \t\r\n");
    const auto last = forwarded.find_last_not_of(L" \t\r\n");
    const auto trimmed = first == std::wstring::npos ? L"" : forwarded.substr(first, last - first + 1);
    const bool verifyOnly = trimmed == L"--verify";
    for (const auto* required : { L"MapleDay.exe", L"MapleDay.dll", L"MapleDay.pri", L"coreclr.dll", L"Microsoft.UI.Xaml.dll" })
    {
        const auto path = appDirectory + L"\\" + required;
        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
        {
            if (!verifyOnly)
                MessageBoxW(nullptr, L"App 폴더의 실행 파일을 찾을 수 없습니다. MapleDay.exe와 App 폴더를 함께 유지해주세요.", L"메요일 · MapleDay", MB_OK | MB_ICONERROR);
            return 2;
        }
    }
    // File-only validation never starts the WinUI app or opens a window.
    if (verifyOnly) return 0;
    auto command = L"\"" + appPath + L"\"";
    if (!forwarded.empty()) command += L" " + forwarded;
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(appPath.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, appDirectory.c_str(), &startup, &process))
    {
        MessageBoxW(nullptr, L"메요일을 시작하지 못했습니다. App 폴더가 함께 있는지 확인해주세요.", L"메요일 · MapleDay", MB_OK | MB_ICONERROR);
        return 3;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}
