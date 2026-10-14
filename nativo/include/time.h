#ifndef _TIME_H
#define _TIME_H

#include <stddef.h>

#ifndef _CS_TIME_T
#define _CS_TIME_T
typedef long long time_t;
#endif
typedef long clock_t;
#define CLOCKS_PER_SEC 1000000L

struct tm {
    int tm_sec, tm_min, tm_hour, tm_mday, tm_mon, tm_year, tm_wday, tm_yday, tm_isdst;
};

#ifndef _CS_TIMESPEC
#define _CS_TIMESPEC
struct timespec { time_t tv_sec; long tv_nsec; };
#endif

#define CLOCK_REALTIME 1
#define CLOCK_MONOTONIC 4
typedef int clockid_t;

time_t time(time_t *t);
clock_t clock(void);
double difftime(time_t fim, time_t inicio);
time_t mktime(struct tm *t);
struct tm *localtime(const time_t *t);
struct tm *localtime_r(const time_t *t, struct tm *r);
struct tm *gmtime(const time_t *t);
struct tm *gmtime_r(const time_t *t, struct tm *r);
size_t strftime(char *s, size_t n, const char *formato, const struct tm *t);
char *asctime(const struct tm *t);
char *ctime(const time_t *t);
int clock_gettime(clockid_t relogio, struct timespec *ts);
int nanosleep(const struct timespec *pedido, struct timespec *resto);

#endif
