/* fcntl.h -- o de sempre, mais o fcntl() da newlib da ESP32 (o Windows nao tem):
 * o dos sockets (src/cs_sockets.cpp), como a VFS do IDF faz. */
#pragma once

#include_next <fcntl.h>

#ifndef O_NONBLOCK
#define O_NONBLOCK 0x4000
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
