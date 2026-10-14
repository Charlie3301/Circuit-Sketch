#ifndef _SYS_TYPES_H
#define _SYS_TYPES_H

#include <stddef.h>
#include <stdint.h>

#ifndef _CS_OFF_T
#define _CS_OFF_T
typedef long off_t;
#endif
#ifndef _CS_SSIZE_T
#define _CS_SSIZE_T
typedef __PTRDIFF_TYPE__ ssize_t;
#endif
typedef int pid_t;
typedef unsigned short uid_t;
typedef unsigned short gid_t;
typedef unsigned long mode_t;
typedef short dev_t;
typedef unsigned short ino_t;
typedef unsigned short nlink_t;
typedef long blksize_t;
typedef long blkcnt_t;
#ifndef _CS_TIME_T
#define _CS_TIME_T
typedef long long time_t;
#endif
typedef long suseconds_t;
typedef unsigned long useconds_t;
typedef unsigned char u_char;
typedef unsigned short u_short;
typedef unsigned int u_int;
typedef unsigned long u_long;
typedef unsigned char u_int8_t;
typedef unsigned short u_int16_t;
typedef unsigned long u_int32_t;
typedef unsigned long long u_int64_t;
typedef long long loff_t;
typedef char *caddr_t;

#endif
