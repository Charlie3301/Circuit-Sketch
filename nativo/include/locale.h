#ifndef _LOCALE_H
#define _LOCALE_H

#define LC_ALL 0
#define LC_NUMERIC 4
struct lconv { char *decimal_point; };
char *setlocale(int categoria, const char *local);
struct lconv *localeconv(void);

#endif
