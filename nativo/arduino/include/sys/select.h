/* sys/select.h -- o fd_set e o select() da newlib da ESP32 (o Windows nao tem):
 * o select() e o dos sockets (src/cs_sockets.cpp), como a VFS do IDF faz. */
#ifndef CS_SYS_SELECT_H
#define CS_SYS_SELECT_H

#include <sys/types.h>
#include <sys/time.h>

#ifndef FD_SETSIZE
#define FD_SETSIZE 64
#endif
#define NFDBITS (8 * sizeof(unsigned long))

typedef struct fd_set {
    unsigned long fds_bits[(FD_SETSIZE + NFDBITS - 1) / NFDBITS];
} fd_set;

#define FD_SET(n, p) do { if ((unsigned)(n) < FD_SETSIZE) (p)->fds_bits[(n) / NFDBITS] |= (1UL << ((n) % NFDBITS)); } while (0)
#define FD_CLR(n, p) do { if ((unsigned)(n) < FD_SETSIZE) (p)->fds_bits[(n) / NFDBITS] &= ~(1UL << ((n) % NFDBITS)); } while (0)
#define FD_ISSET(n, p) (((unsigned)(n) < FD_SETSIZE) ? ((p)->fds_bits[(n) / NFDBITS] & (1UL << ((n) % NFDBITS))) : 0)
#define FD_ZERO(p) do { for (unsigned cs_i = 0; cs_i < sizeof((p)->fds_bits) / sizeof((p)->fds_bits[0]); cs_i++) (p)->fds_bits[cs_i] = 0; } while (0)

#ifdef __cplusplus
extern "C" {
#endif
int select(int nfds, fd_set *readfds, fd_set *writefds, fd_set *errorfds, struct timeval *timeout);
#ifdef __cplusplus
}
#endif

#endif
