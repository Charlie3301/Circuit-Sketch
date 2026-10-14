/* Pastas do cartao SD e da flash simulados. */
#ifndef _DIRENT_H
#define _DIRENT_H

#include <sys/types.h>

#define DT_UNKNOWN 0
#define DT_REG 1
#define DT_DIR 2

struct dirent {
    ino_t d_ino;
    unsigned char d_type;
    char d_name[256];
};

typedef struct cs_pasta DIR;

DIR *opendir(const char *caminho);
struct dirent *readdir(DIR *pasta);
int closedir(DIR *pasta);
void rewinddir(DIR *pasta);
long telldir(DIR *pasta);
void seekdir(DIR *pasta, long pos);

#endif
