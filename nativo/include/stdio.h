/* stdio.h do CircuitSketch.
 *
 * Os arquivos sao os do cartao SD e da flash simulados (montados em
 * /sdcard, /spiffs...); stdout e stderr vao para o monitor serial, como
 * na ESP32, onde o printf sai pela UART0. */
#ifndef _STDIO_H
#define _STDIO_H

#include <stddef.h>
#include <stdarg.h>

typedef struct cs_arquivo FILE;
typedef long long fpos_t;
#ifndef _CS_OFF_T
#define _CS_OFF_T
typedef long off_t;
#endif

extern FILE *const stdin;
extern FILE *const stdout;
extern FILE *const stderr;

#define EOF (-1)
#define BUFSIZ 512
#define FILENAME_MAX 256
#define FOPEN_MAX 32
#define L_tmpnam 32
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2
#define _IOFBF 0
#define _IOLBF 1
#define _IONBF 2

FILE *fopen(const char *caminho, const char *modo);
FILE *freopen(const char *caminho, const char *modo, FILE *f);
FILE *fdopen(int fd, const char *modo);
int fclose(FILE *f);
size_t fread(void *buf, size_t tamanho, size_t quantos, FILE *f);
size_t fwrite(const void *buf, size_t tamanho, size_t quantos, FILE *f);
int fseek(FILE *f, long desloc, int de_onde);
long ftell(FILE *f);
void rewind(FILE *f);
int fgetpos(FILE *f, fpos_t *pos);
int fsetpos(FILE *f, const fpos_t *pos);
int feof(FILE *f);
int ferror(FILE *f);
void clearerr(FILE *f);
int fflush(FILE *f);
int fileno(FILE *f);
int setvbuf(FILE *f, char *buf, int modo, size_t tamanho);
void setbuf(FILE *f, char *buf);
int fgetc(FILE *f);
int getc(FILE *f);
int getchar(void);
char *fgets(char *s, int n, FILE *f);
int ungetc(int c, FILE *f);
int fputc(int c, FILE *f);
int putc(int c, FILE *f);
int putchar(int c);
int fputs(const char *s, FILE *f);
int puts(const char *s);
int remove(const char *caminho);
int rename(const char *velho, const char *novo);
FILE *tmpfile(void);
char *tmpnam(char *s);

int printf(const char *formato, ...);
int fprintf(FILE *f, const char *formato, ...);
int sprintf(char *s, const char *formato, ...);
int snprintf(char *s, size_t n, const char *formato, ...);
int vprintf(const char *formato, va_list ap);
int vfprintf(FILE *f, const char *formato, va_list ap);
int vsprintf(char *s, const char *formato, va_list ap);
int vsnprintf(char *s, size_t n, const char *formato, va_list ap);
int asprintf(char **s, const char *formato, ...);
int vasprintf(char **s, const char *formato, va_list ap);
int sscanf(const char *s, const char *formato, ...);
int vsscanf(const char *s, const char *formato, va_list ap);
int fscanf(FILE *f, const char *formato, ...);
int scanf(const char *formato, ...);
void perror(const char *s);

#endif
