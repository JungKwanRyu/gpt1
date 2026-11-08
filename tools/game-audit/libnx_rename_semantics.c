/* Test-only POSIX fault injection: reproduce Horizon's EEXIST on rename over
 * an existing destination for the exact .switch-temp patch replacement path.
 * Other rename operations retain the real libc behavior. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <string.h>
#include <unistd.h>

static int reject(const char *source, const char *target)
{
    const char *suffix = ".switch-temp";
    size_t length = strlen(source), size = strlen(suffix);
    if (length >= size && !strcmp(source + length - size, suffix) && access(target, F_OK) == 0) {
        errno = EEXIST;
        return 1;
    }
    return 0;
}

int rename(const char *source, const char *target)
{
    if (reject(source, target)) return -1;
    int (*original)(const char *, const char *) = dlsym(RTLD_NEXT, "rename");
    return original(source, target);
}
