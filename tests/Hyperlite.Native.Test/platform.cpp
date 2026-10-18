#include "platform.h"

#include <cstdio>
#include <string>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#else
#include <dlfcn.h>
#include <limits.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <unistd.h>
#endif

using namespace hyperlite::abi;

using entry_t = std::int32_t(HYPERLITE_CALL*)(std::int32_t, const t_dispatch**);

#if defined(_WIN32)

const t_dispatch* load_dispatch()
{
    // the executable's directory is searched first
    HMODULE lib = ::LoadLibraryW(L"Hyperlite.Native.win-x64.dll");
    if (!lib)
    {
        std::printf("FAIL LoadLibrary %lu\n", ::GetLastError());
        return nullptr;
    }

    // exported by ordinal only, see Hyperlite.Native.def
    auto entry = reinterpret_cast<entry_t>(::GetProcAddress(lib, reinterpret_cast<LPCSTR>(1)));
    if (!entry)
    {
        std::printf("FAIL GetProcAddress by ordinal %lu\n", ::GetLastError());
        return nullptr;
    }

    const t_dispatch* d = nullptr;
    if (entry(k_abi_revision, &d) != 0 || !d)
    {
        std::printf("FAIL bootstrap\n");
        return nullptr;
    }
    return d;
}

void remove_file(const char* path)
{
    ::DeleteFileA(path);
}

bool read_locked(const char* path)
{
    const HANDLE file = ::CreateFileA(path, GENERIC_READ | GENERIC_WRITE,
                                      FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                      nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return false;
    }
    OVERLAPPED at{};
    at.Offset = 0x40000002;
    const bool locked =
        !::LockFileEx(file, LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY, 0, 510, 0, &at);
    if (!locked)
    {
        ::UnlockFileEx(file, 0, 510, 0, &at);
    }
    ::CloseHandle(file);
    return locked;
}

#else

const t_dispatch* load_dispatch()
{
    // dlopen does not search the executable's directory, so the path is spelled out
    char exe[PATH_MAX];
    const ssize_t n = ::readlink("/proc/self/exe", exe, sizeof(exe) - 1);
    if (n <= 0)
    {
        std::printf("FAIL readlink /proc/self/exe\n");
        return nullptr;
    }
    std::string path(exe, static_cast<std::size_t>(n));
    path = path.substr(0, path.rfind('/') + 1) + "libHyperlite.Native.linux-x64.so";

    void* lib = ::dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!lib)
    {
        std::printf("FAIL dlopen %s\n", ::dlerror());
        return nullptr;
    }

    auto entry = reinterpret_cast<entry_t>(::dlsym(lib, "q4kf9wz"));
    if (!entry)
    {
        std::printf("FAIL dlsym %s\n", ::dlerror());
        return nullptr;
    }

    const t_dispatch* d = nullptr;
    if (entry(k_abi_revision, &d) != 0 || !d)
    {
        std::printf("FAIL bootstrap\n");
        return nullptr;
    }
    return d;
}

void remove_file(const char* path)
{
    ::unlink(path);
}

bool read_locked(const char* path)
{
    // Not by a lock of our own: sqlite's are POSIX record locks, which never conflict within
    // one process, and closing any descriptor of the file would drop all of them. The
    // kernel lists them in /proc/locks instead, by device and inode.
    struct stat st{};
    if (::stat(path, &st) != 0)
    {
        return false;
    }
    std::FILE* locks = std::fopen("/proc/locks", "r");
    if (!locks)
    {
        return false;
    }
    const long long shared_first = 0x40000002;
    bool locked = false;
    char line[256];
    while (!locked && std::fgets(line, sizeof(line), locks))
    {
        // "1: POSIX  ADVISORY  READ 1234 08:20:5678 1073741826 1073742335"
        char kind[16];
        char mode[16];
        char access[16];
        int pid = 0;
        unsigned int major_id = 0;
        unsigned int minor_id = 0;
        unsigned long inode = 0;
        long long start = 0;
        if (std::sscanf(line, "%*d: %15s %15s %15s %d %x:%x:%lu %lld", kind, mode, access, &pid,
                        &major_id, &minor_id, &inode, &start) == 8)
        {
            locked = pid == ::getpid() && std::string(access) == "READ" &&
                     major_id == major(st.st_dev) && minor_id == minor(st.st_dev) &&
                     inode == st.st_ino && start == shared_first;
        }
    }
    std::fclose(locks);
    return locked;
}

#endif
