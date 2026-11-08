/* sys/time.h -- o da newlib (ESP32, Pico): o do Windows nao tem o settimeofday,
 * que acerta o relogio do sistema (src/cs_idf_relogio.cpp). */
#ifndef CS_SYS_TIME_H
#define CS_SYS_TIME_H

#include_next <sys/time.h>

struct timezone;
#ifdef __cplusplus
extern "C"
#endif
int settimeofday(const struct timeval *tv, const struct timezone *tz);

#endif
