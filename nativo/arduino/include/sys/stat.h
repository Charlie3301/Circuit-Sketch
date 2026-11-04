/* sys/stat.h -- o do compilador, com o stat, o mkdir (de dois argumentos, como na
 * newlib) e o rmdir passando pelos sistemas de arquivos montados do esp_vfs
 * (src/cs_idf_vfs.cpp). */
#ifndef CS_SYS_STAT_H
#define CS_SYS_STAT_H
#include_next <sys/stat.h>

#if defined(ESP32) && !defined(CS_VFS_INTERNO)
#include <direct.h>
#ifdef __cplusplus
extern "C" {
#endif
int cs_vfs_stat(const char *caminho, struct stat *st);
int cs_vfs_mkdir(const char *caminho, int modo);
int cs_vfs_rmdir(const char *caminho);
#ifdef __cplusplus
}
#endif
#define stat(caminho, st) cs_vfs_stat((caminho), (st))
#define mkdir(caminho, modo) cs_vfs_mkdir((caminho), (modo))
#define rmdir(caminho) cs_vfs_rmdir(caminho)
#endif
#endif
