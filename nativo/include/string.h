#ifndef _STRING_H
#define _STRING_H

#include <stddef.h>

void *memcpy(void *destino, const void *origem, size_t n);
void *memmove(void *destino, const void *origem, size_t n);
void *memset(void *destino, int c, size_t n);
int memcmp(const void *a, const void *b, size_t n);
void *memchr(const void *s, int c, size_t n);
void *memrchr(const void *s, int c, size_t n);
void *memmem(const void *palheiro, size_t n1, const void *agulha, size_t n2);

size_t strlen(const char *s);
size_t strnlen(const char *s, size_t n);
char *strcpy(char *destino, const char *origem);
char *strncpy(char *destino, const char *origem, size_t n);
size_t strlcpy(char *destino, const char *origem, size_t n);
size_t strlcat(char *destino, const char *origem, size_t n);
char *strcat(char *destino, const char *origem);
char *strncat(char *destino, const char *origem, size_t n);
int strcmp(const char *a, const char *b);
int strncmp(const char *a, const char *b, size_t n);
int strcoll(const char *a, const char *b);
char *strchr(const char *s, int c);
char *strrchr(const char *s, int c);
char *strstr(const char *palheiro, const char *agulha);
char *strcasestr(const char *palheiro, const char *agulha);
size_t strspn(const char *s, const char *aceitos);
size_t strcspn(const char *s, const char *recusados);
char *strpbrk(const char *s, const char *aceitos);
char *strtok(char *s, const char *separadores);
char *strtok_r(char *s, const char *separadores, char **resto);
char *strsep(char **s, const char *separadores);
char *strdup(const char *s);
char *strndup(const char *s, size_t n);
char *strerror(int erro);
char *strlwr(char *s);
char *strupr(char *s);
size_t strxfrm(char *destino, const char *origem, size_t n);

#include <strings.h>

#endif
