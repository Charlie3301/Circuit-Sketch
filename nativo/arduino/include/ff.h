/* ff.h -- o do FatFs (esp_vfs_fat.h inclui), sem o caminho de Windows dele: o
 * programa e o da placa, mesmo compilado no computador. */
#ifndef CS_FF_H
#define CS_FF_H
#ifndef MIN
#define MIN(a, b) (((a) < (b)) ? (a) : (b))
#endif
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <sys/types.h>
#include <time.h>
#pragma push_macro("_WIN32")
#undef _WIN32
#include_next <ff.h>
#pragma pop_macro("_WIN32")
#endif
