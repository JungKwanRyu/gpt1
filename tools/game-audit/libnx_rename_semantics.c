/* Test-only POSIX fault injection: reproduce Horizon's EEXIST on rename over
 * an existing destination for the exact .switch-temp patch replacement path.
 * Other rename operations retain the real libc behavior. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <string.h>
#include <unistd.h>
#include <stdio.h>
#include <sys/stat.h>

static int reject(const char *source, const char *target)
{
    const char *suffix = ".switch-temp";
    size_t length = strlen(source), size = strlen(suffix);
    int cache_target = strstr(target, "/output/game-audit/") != NULL || !strncmp(target, "output/game-audit/", 18);
    if (cache_target && strstr(source, ".wog-fail-move")) { errno = EIO; return 1; }
    if (((length >= size && !strcmp(source + length - size, suffix)) || cache_target) && access(target, F_OK) == 0) {
        errno = EEXIST;
        return 1;
    }
    return 0;
}

int fchmod(int fd, mode_t mode)
{
    char descriptor[64], target[4096];
    snprintf(descriptor, sizeof(descriptor), "/proc/self/fd/%d", fd);
    ssize_t length = readlink(descriptor, target, sizeof(target) - 1);
    if (length >= 0) {
        target[length] = 0;
        if (strstr(target, "/output/game-audit/")) { errno = ENOSYS; return -1; }
    }
    int (*original)(int, mode_t) = dlsym(RTLD_NEXT, "fchmod");
    return original(fd, mode);
}

int rename(const char *source, const char *target)
{
    if (reject(source, target)) return -1;
    int (*original)(const char *, const char *) = dlsym(RTLD_NEXT, "rename");
    return original(source, target);
}
