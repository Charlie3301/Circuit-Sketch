/* dirent.h -- as pastas como na newlib da ESP32 (com o d_type), nos sistemas de
 * arquivos montados pelo esp_vfs (src/cs_idf_vfs.cpp). */
#ifndef CS_DIRENT_H
#define CS_DIRENT_H

#ifdef __cplusplus
extern "C" {
#endif

#define DT_UNKNOWN 0
#define DT_REG 1
#define DT_DIR 2

struct dirent {
    int d_ino;
    unsigned char d_type;
    char d_name[256];
};
typedef struct cs_dir_opaco DIR;

void *cs_vfs_opendir(const char *caminho);
void *cs_vfs_readdir(void *dir);
int cs_vfs_closedir(void *dir);
void cs_vfs_rewinddir(void *dir);

static inline DIR *opendir(const char *caminho) { return (DIR *)cs_vfs_opendir(caminho); }
static inline struct dirent *readdir(DIR *dir) { return (struct dirent *)cs_vfs_readdir(dir); }
static inline int closedir(DIR *dir) { return cs_vfs_closedir(dir); }
static inline void rewinddir(DIR *dir) { cs_vfs_rewinddir(dir); }

#ifdef __cplusplus
}
#endif
#endif
