#pragma once
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <algorithm>
#include <stdexcept>
#include <set>

namespace updates {
namespace fs = std::filesystem;
struct owned_handle {
    HANDLE value;
    ~owned_handle() { if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value); }
};
inline std::wstring read(const fs::path& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("read");
    std::vector<char> bytes((std::istreambuf_iterator<char>(input)), {});
    if (bytes.size() < 2 || bytes.size() > 2 * 1024 * 1024 || bytes.size() % 2 ||
        static_cast<unsigned char>(bytes[0]) != 0xff || static_cast<unsigned char>(bytes[1]) != 0xfe) throw std::runtime_error("encoding");
    std::wstring result((bytes.size() - 2) / 2, L'\0');
    memcpy(result.data(), bytes.data() + 2, bytes.size() - 2);
    return result;
}
inline void write(const fs::path& path, const std::wstring& text) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("write");
    DWORD written = 0;
    const wchar_t bom = 0xfeff;
    const DWORD size = static_cast<DWORD>(text.size() * 2);
    const bool ok = WriteFile(file, &bom, 2, &written, nullptr) && written == 2 &&
        WriteFile(file, text.data(), size, &written, nullptr) && written == size && FlushFileBuffers(file);
    CloseHandle(file);
    if (!ok) throw std::runtime_error("write");
}
inline void flush(const fs::path& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("flush");
    const bool ok = FlushFileBuffers(file); CloseHandle(file);
    if (!ok) throw std::runtime_error("flush");
}
inline bool safe(const std::wstring& path) {
    if (path == L"MapleDay.exe") return true;
    if (path.rfind(L"App/", 0) != 0 || path.size() > 220) return false;
    std::wistringstream parts(path);
    std::wstring part;
    while (std::getline(parts, part, L'/')) {
        if (part.empty() || part.front() == L'.' || part.back() == L'.' || part.back() == L' ') return false;
        std::wstring upper = part;
        std::transform(upper.begin(), upper.end(), upper.begin(), towupper);
        const auto stem = upper.substr(0, upper.find(L'.'));
        if (upper == L"UNINSTALL" || stem == L"CON" || stem == L"PRN" || stem == L"AUX" || stem == L"NUL" ||
            (stem.size() == 4 && (stem.rfind(L"COM", 0) == 0 || stem.rfind(L"LPT", 0) == 0) && stem[3] >= L'1' && stem[3] <= L'9')) return false;
        for (const auto c : part) if (c < 32 || std::wstring(L"\\:*?\"<>|").find(c) != std::wstring::npos) return false;
    }
    return path.back() != L'/';
}
inline fs::path resolve(const fs::path& root, const std::wstring& relative) {
    if (!safe(relative)) throw std::runtime_error("path");
    auto current = root;
    const auto check = [](const fs::path& path) {
        const auto attributes = GetFileAttributesW(path.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT)) throw std::runtime_error("link");
    };
    check(current);
    std::wistringstream parts(relative);
    std::wstring part;
    while (std::getline(parts, part, L'/')) { current /= part; check(current); }
    return current;
}
struct operation { wchar_t action; std::wstring path, hash; unsigned long long size; };
inline std::vector<operation> plan(const fs::path& job) {
    std::wistringstream lines(read(job / L"plan.txt"));
    std::wstring line;
    std::vector<operation> result;
    std::set<std::wstring> paths;
    while (std::getline(lines, line)) {
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        if (line.empty()) continue;
        std::wistringstream columns(line);
        std::wstring action, path, hash, size;
        std::getline(columns, action, L'\t'); std::getline(columns, path, L'\t');
        std::getline(columns, hash, L'\t'); std::getline(columns, size, L'\t');
        auto lower = path; std::transform(lower.begin(), lower.end(), lower.begin(), towlower);
        if ((action != L"R" && action != L"D") || !safe(path) || !paths.insert(lower).second || result.size() >= 5000 || size.empty() ||
            size.find_first_not_of(L"0123456789") != std::wstring::npos || (action == L"R" && (hash.size() != 64 || hash.find_first_not_of(L"0123456789abcdef") != std::wstring::npos))) throw std::runtime_error("plan");
        result.push_back({ action[0], path, hash, std::stoull(size) });
    }
    return result;
}
inline std::wstring hash(const fs::path& path) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE handle = nullptr;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) throw std::runtime_error("hash");
    std::vector<unsigned char> object;
    DWORD size = 0, received = 0;
    bool ok = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&size), sizeof(size), &received, 0) >= 0;
    object.resize(size);
    ok = ok && BCryptCreateHash(algorithm, &handle, object.data(), size, nullptr, 0, 0) >= 0;
    std::ifstream file(path, std::ios::binary);
    ok = ok && file.good();
    std::vector<unsigned char> buffer(81920);
    while (ok && file) {
        file.read(reinterpret_cast<char*>(buffer.data()), static_cast<std::streamsize>(buffer.size()));
        const auto count = file.gcount();
        if (count > 0) ok = BCryptHashData(handle, buffer.data(), static_cast<ULONG>(count), 0) >= 0;
    }
    unsigned char digest[32]{};
    ok = ok && !file.bad() && BCryptFinishHash(handle, digest, 32, 0) >= 0;
    if (handle) BCryptDestroyHash(handle);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!ok) throw std::runtime_error("hash");
    const wchar_t* hex = L"0123456789abcdef";
    std::wstring result;
    for (const auto byte : digest) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}
inline void validate(const fs::path& root, const fs::path& job, const std::vector<operation>& operations) {
    for (const auto& item : operations) {
        resolve(root, item.path);
        if (item.action == L'R') {
            const auto source = resolve(job / L"files", item.path);
            if (fs::file_size(source) != item.size || hash(source) != item.hash) throw std::runtime_error("integrity");
        }
    }
}
inline fs::path pending_path(const fs::path& root, const fs::path& destination) {
    // The launcher replacement also stages inside App, keeping the root clean.
    return destination == root / L"MapleDay.exe" ? root / L"App" / L".update-launcher-new"
        : destination.parent_path() / (L"." + destination.filename().wstring() + L".mapleday-update");
}
inline void replace(const fs::path& root, const fs::path& source, const fs::path& destination) {
    const auto pending = pending_path(root, destination);
    fs::create_directories(destination.parent_path());
    if (fs::exists(pending)) throw std::runtime_error("pending");
    if (!CopyFileW(source.c_str(), pending.c_str(), TRUE)) throw std::runtime_error("replace");
    flush(pending);
    // A same-volume atomic rename never leaves MapleDay.exe missing/truncated.
    if (!MoveFileExW(pending.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) throw std::runtime_error("replace");
}
inline void rollback(const fs::path& root, const fs::path& job, const std::vector<operation>& operations) {
    // An operation is backed up before it can be modified. Unprepared ones stay untouched.
    for (auto it = operations.rbegin(); it != operations.rend(); ++it) {
        const auto destination = resolve(root, it->path);
        const auto backup = resolve(job / L"backup", it->path);
        const auto pending = pending_path(root, destination);
        if (fs::exists(pending) && !DeleteFileW(pending.c_str())) throw std::runtime_error("restore");
        if (fs::exists(backup)) {
            if (fs::exists(destination) && fs::file_size(destination) == fs::file_size(backup) && hash(destination) == hash(backup)) continue;
            replace(root, backup, destination);
        } else if (fs::exists(resolve(job / L"missing", it->path))) {
            if (fs::exists(destination) && !DeleteFileW(destination.c_str())) throw std::runtime_error("restore");
        }
    }
}
inline void apply(const fs::path& root, const fs::path& job, const std::vector<operation>& operations) {
    validate(root, job, operations);
    fs::create_directories(job / L"backup");
    // Persist a recovery pointer only after the immutable plan and staging passed validation.
    write(root / L"App" / L".update-job", job.wstring());
    try {
        for (const auto& item : operations) {
            const auto destination = resolve(root, item.path);
            const auto backup = resolve(job / L"backup", item.path);
            fs::create_directories(backup.parent_path());
            if (fs::exists(destination)) {
                if (!CopyFileW(destination.c_str(), backup.c_str(), TRUE)) throw std::runtime_error("backup");
                flush(backup);
            } else {
                const auto marker = resolve(job / L"missing", item.path);
                fs::create_directories(marker.parent_path()); write(marker, L"missing");
            }
            if (item.action == L'R') {
                const auto source = resolve(job / L"files", item.path);
                replace(root, source, destination);
            } else if (fs::exists(destination) && !DeleteFileW(destination.c_str())) throw std::runtime_error("locked");
        }
        write(job / L"committed", L"committed");
    } catch (...) {
        try { fs::remove(job / L"committed"); rollback(root, job, operations); fs::remove(root / L"App" / L".update-job"); }
        catch (...) { throw std::runtime_error("recovery"); }
        throw;
    }
    fs::remove(root / L"App" / L".update-job");
}
inline bool launch(const fs::path& root) {
    auto application = root / L"MapleDay.exe";
    auto command = L"\"" + application.wstring() + L"\"";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(application.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process)) return false;
    CloseHandle(process.hThread); CloseHandle(process.hProcess); return true;
}
inline int worker(const fs::path& job, DWORD parent, bool recover) {
    try {
        const fs::path root = read(job / L"root.txt");
        if (!root.is_absolute() || !job.is_absolute() || !fs::is_directory(root / L"App")) return 4;
        const auto operations = plan(job);
        owned_handle lock{CreateFileW((root / L"App" / L".update-lock").c_str(), GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr)};
        if (lock.value == INVALID_HANDLE_VALUE) throw std::runtime_error("lock");
        HANDLE process = nullptr;
        if (!recover) {
            process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, parent);
            if (!process) throw std::runtime_error("parent");
            std::vector<wchar_t> name(32768); DWORD size = static_cast<DWORD>(name.size());
            if (!QueryFullProcessImageNameW(process, 0, name.data(), &size) || !fs::equivalent(fs::path(std::wstring(name.data(), size)), root / L"App" / L"MapleDay.exe")) {
                CloseHandle(process); throw std::runtime_error("parent");
            }
            try { validate(root, job, operations); write(root / L"App" / L".update-probe", L"probe"); fs::remove(root / L"App" / L".update-probe"); write(job / L"status.txt", L"ready"); }
            catch (...) { CloseHandle(process); throw; }
            const auto waited = WaitForSingleObject(process, 120000); CloseHandle(process);
            if (waited != WAIT_OBJECT_0) throw std::runtime_error("timeout");
        }
        bool succeeded = false;
        try {
            if (recover) {
                if (!fs::exists(job / L"committed")) rollback(root, job, operations);
                fs::remove(root / L"App" / L".update-job");
            } else apply(root, job, operations);
            succeeded = true;
        } catch (...) { write(job / L"status.txt", L"error"); }
        CloseHandle(lock.value); lock.value = INVALID_HANDLE_VALUE;
        fs::remove(root / L"App" / L".update-lock");
        if (fs::exists(root / L"App" / L".update-job")) return 5;
        if (!launch(root)) return 6;
        // Running worker.exe stays until next app cleanup; backup/files can be removed now.
        std::error_code ignored;
        fs::remove_all(job / L"backup", ignored); fs::remove_all(job / L"files", ignored); fs::remove_all(job / L"missing", ignored);
        return succeeded ? 0 : 4;
    } catch (...) { try { write(job / L"status.txt", L"error"); } catch (...) {} return 4; }
}
}
