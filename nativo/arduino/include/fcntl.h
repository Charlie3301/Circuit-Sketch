/* fcntl.h -- o de sempre, mais o fcntl() da newlib da ESP32 (o Windows nao tem):
 * o dos sockets (src/cs_sockets.cpp), como a VFS do IDF faz. */
#pragma once

#include_next <fcntl.h>

#ifndef O_NONBLOCK
#define O_NONBLOCK 0x4000
#endif
/* Os da newlib que o Windows nao tem (o F_GETFL e o F_SETFL vem do lwip/sockets.h). */
#ifndef F_GETFD
#define F_GETFD 1
#endif
#ifndef F_SETFD
#define F_SETFD 2
#endif
#ifndef FD_CLOEXEC
#define FD_CLOEXEC 1
#endif
#ifndef O_SYNC
#define O_SYNC 0x2000
#endif

#ifdef __cplusplus
extern "C" {
#endif
int fcntl(int fd, int cmd, ...);
#ifdef __cplusplus
}
#endif
