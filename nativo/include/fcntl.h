#ifndef _FCNTL_H
#define _FCNTL_H

#include <sys/types.h>

#define O_RDONLY 0
#define O_WRONLY 1
#define O_RDWR 2
#define O_ACCMODE 3
#define O_APPEND 0x0008
#define O_CREAT 0x0200
#define O_TRUNC 0x0400
#define O_EXCL 0x0800
#define O_NONBLOCK 0x4000
#define O_BINARY 0
#define O_TEXT 0

#define F_GETFL 3
#define F_SETFL 4

int open(const char *caminho, int flags, ...);
int fcntl(int fd, int comando, ...);

#endif
