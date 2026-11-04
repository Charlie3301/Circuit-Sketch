/* fcntl.h -- o de sempre, mais o fcntl() da newlib da ESP32 (o Windows nao tem):
 * o dos sockets (src/cs_sockets.cpp), como a VFS do IDF faz. */
#pragma once

#include_next <fcntl.h>

#ifdef __cplusplus
extern "C" {
#endif
int fcntl(int fd, int cmd, ...);
#ifdef __cplusplus
}
#endif
