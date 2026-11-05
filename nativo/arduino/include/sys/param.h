/* sys/param.h -- o da newlib da ESP32: o do Windows nao tem o MIN, o MAX e o
 * howmany, que o codigo do IDF (o esp_http_server, o console) usa. */
#ifndef CS_SYS_PARAM_H
#define CS_SYS_PARAM_H

#include_next <sys/param.h>

#ifndef MIN
#define MIN(a, b) (((a) < (b)) ? (a) : (b))
#endif
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif
#ifndef howmany
#define howmany(x, y) (((x) + ((y) - 1)) / (y))
#endif
#ifndef roundup
#define roundup(x, y) ((((x) + ((y) - 1)) / (y)) * (y))
#endif
#ifndef powerof2
#define powerof2(x) ((((x) - 1) & (x)) == 0)
#endif
#ifndef HZ
#define HZ 100
#endif

#endif
