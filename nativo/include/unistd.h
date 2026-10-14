#ifndef _UNISTD_H
#define _UNISTD_H

#include <sys/types.h>

#define STDIN_FILENO 0
#define STDOUT_FILENO 1
#define STDERR_FILENO 2
#define F_OK 0
#define X_OK 1
#define W_OK 2
#define R_OK 4
#ifndef SEEK_SET
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2
#endif

int open(const char *caminho, int flags, ...);
int close(int fd);
ssize_t read(int fd, void *buf, size_t n);
ssize_t write(int fd, const void *buf, size_t n);
off_t lseek(int fd, off_t desloc, int de_onde);
ssize_t pread(int fd, void *buf, size_t n, off_t desloc);
ssize_t pwrite(int fd, const void *buf, size_t n, off_t desloc);
int access(const char *caminho, int modo);
int unlink(const char *caminho);
int rmdir(const char *caminho);
int chdir(const char *caminho);
char *getcwd(char *buf, size_t n);
int usleep(useconds_t us);
unsigned int sleep(unsigned int segundos);
int fsync(int fd);
int ftruncate(int fd, off_t n);
int truncate(const char *caminho, off_t n);
int isatty(int fd);
int dup(int fd);
int dup2(int fd, int novo);
pid_t getpid(void);
uid_t getuid(void);
uid_t geteuid(void);
int gethostname(char *nome, size_t n);
long sysconf(int nome);
int link(const char *velho, const char *novo);

#endif
