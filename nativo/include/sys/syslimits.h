/* sys/syslimits.h do CircuitSketch (codigo nativo): os limites POSIX da
 * newlib do ESP32, que o limits.h traz junto. */
#ifndef _SYS_SYSLIMITS_H_
#define _SYS_SYSLIMITS_H_

#define ARG_MAX 65536
#ifndef CHILD_MAX
#define CHILD_MAX 40
#endif
#define LINK_MAX 32767
#define MAX_CANON 255
#define MAX_INPUT 255
#define NAME_MAX 255
#define NGROUPS_MAX 16
#ifndef OPEN_MAX
#define OPEN_MAX 64
#endif
#define PATH_MAX 1024
#define PIPE_BUF 512
#define IOV_MAX 1024

#define BC_BASE_MAX 99
#define BC_DIM_MAX 2048
#define BC_SCALE_MAX 99
#define BC_STRING_MAX 1000
#define COLL_WEIGHTS_MAX 0
#define EXPR_NEST_MAX 32
#define LINE_MAX 2048
#define RE_DUP_MAX 255

#endif
