/* ff.h -- o do FatFs (esp_vfs_fat.h inclui), sem o caminho de Windows dele: o
 * programa e o da placa, mesmo compilado no computador. */
#ifndef CS_FF_H
#define CS_FF_H
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif
#pragma push_macro("_WIN32")
#undef _WIN32
#include_next <ff.h>
#pragma pop_macro("_WIN32")
#endif
