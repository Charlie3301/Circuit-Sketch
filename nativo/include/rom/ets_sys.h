#ifndef CS_ETS_SYS_H
#define CS_ETS_SYS_H

#include <stdint.h>

int ets_printf(const char *formato, ...);
void ets_delay_us(uint32_t us);
#define ets_install_putc1(f) ((void)0)

#endif
