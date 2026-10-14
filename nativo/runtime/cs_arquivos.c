/* cs_arquivos.c -- stdio, os arquivos do POSIX, as pastas e a flash.
 *
 * Os arquivos moram no simulador: "/sdcard/..." e o cartao microSD do
 * circuito (montado com esp_vfs_fat_sdspi_mount), "/spiffs/..." e a flash.
 * stdout e stderr vao para o monitor serial, como o printf da ESP32 vai
 * para a UART0; stdin le o que a pessoa digita no monitor. */
#include <stdint.h>
#include <stddef.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <dirent.h>
#include <sys/stat.h>
#include "cs_host.h"
#include "cs_interno.h"
#include "esp_err.h"
#include "esp_partition.h"
#include "esp_flash.h"
#include "spi_flash_mmap.h"
#include "esp_heap_caps.h"

#define TAMANHO_DO_BUFFER 4096

enum { ARQ_DISCO = 0, ARQ_SAIDA = 1, ARQ_ENTRADA = 2 };

struct cs_arquivo {
    int tipo;
    int64_t alca;
    int modo;
    int fim, erro;
    int devolvido;
    unsigned char *buf;
    int buf_pos, buf_len;
    int64_t pos_buf;
    int fd;
};

static struct cs_arquivo saida_padrao = { ARQ_SAIDA, 0, CS_ARQ_ESCREVER, 0, 0, -1, NULL, 0, 0, 0, 1 };
static struct cs_arquivo erro_padrao = { ARQ_SAIDA, 0, CS_ARQ_ESCREVER, 0, 0, -1, NULL, 0, 0, 0, 2 };
static struct cs_arquivo entrada_padrao = { ARQ_ENTRADA, 0, CS_ARQ_LER, 0, 0, -1, NULL, 0, 0, 0, 0 };

FILE *const stdout = &saida_padrao;
FILE *const stderr = &erro_padrao;
FILE *const stdin = &entrada_padrao;

/* ---------------------------------------------------------------------
 * descritores do POSIX: 0, 1 e 2 sao a serial; do 3 em diante, arquivos */
#define MAX_FDS 64
static struct { int usado; int64_t alca; int modo; FILE *dono; } fds[MAX_FDS];
static cs_giro trava_dos_fds;

void cs_arquivos_iniciar(void)
{
    memset(fds, 0, sizeof(fds));
    fds[0].usado = fds[1].usado = fds[2].usado = 1;
}

static int novo_fd(int64_t alca, int modo)
{
    cs_girar(&trava_dos_fds);
    for (int i = 3; i < MAX_FDS; i++)
        if (!fds[i].usado) {
            fds[i].usado = 1;
            fds[i].alca = alca;
            fds[i].modo = modo;
            fds[i].dono = NULL;
            cs_soltar(&trava_dos_fds);
            return i;
        }
    cs_soltar(&trava_dos_fds);
    return -1;
}

static int fd_valido(int fd)
{
    return fd >= 0 && fd < MAX_FDS && fds[fd].usado;
}

static int modo_de_texto(const char *m)
{
    int modo = 0;
    int mais = strchr(m, '+') != NULL;
    switch (m[0]) {
    case 'r': modo = CS_ARQ_LER | (mais ? CS_ARQ_ESCREVER : 0); break;
    case 'w': modo = CS_ARQ_ESCREVER | CS_ARQ_CRIAR | CS_ARQ_TRUNCAR | (mais ? CS_ARQ_LER : 0); break;
    case 'a': modo = CS_ARQ_ESCREVER | CS_ARQ_CRIAR | CS_ARQ_ANEXAR | (mais ? CS_ARQ_LER : 0); break;
    default: return -1;
    }
    if (strchr(m, 'x')) modo |= CS_ARQ_EXCLUSIVO;
    return modo;
}

static int modo_de_flags(int flags)
{
    int modo = 0;
    switch (flags & O_ACCMODE) {
    case O_RDONLY: modo = CS_ARQ_LER; break;
    case O_WRONLY: modo = CS_ARQ_ESCREVER; break;
    default: modo = CS_ARQ_LER | CS_ARQ_ESCREVER; break;
    }
    if (flags & O_CREAT) modo |= CS_ARQ_CRIAR;
    if (flags & O_TRUNC) modo |= CS_ARQ_TRUNCAR;
    if (flags & O_APPEND) modo |= CS_ARQ_ANEXAR;
    if (flags & O_EXCL) modo |= CS_ARQ_EXCLUSIVO;
    return modo;
}

static int64_t abrir_no_simulador(const char *caminho, int modo)
{
    int64_t a = cs->arq_abrir(caminho, modo);
    CS_CONFERIR();
    if (a < 0) {
        errno = (int)-a;
        return -1;
    }
    return a;
}

/* ---------------------------------------------------------------------
 * a serial */
static void escrever_na_serial(const char *p, int n)
{
    if (n > 0) cs->escrever(p, n);
    CS_CONFERIR();
}

static int ler_da_serial(void)
{
    int c = cs->ler_serial();
    CS_CONFERIR();
    return c;
}

/* ---------------------------------------------------------------------
 * FILE */
FILE *fopen(const char *caminho, const char *m)
{
    int modo = modo_de_texto(m ? m : "r");
    if (modo < 0) {
        errno = EINVAL;
        return NULL;
    }
    int64_t a = abrir_no_simulador(caminho, modo);
    if (a < 0) return NULL;

    FILE *f = (FILE *)malloc(sizeof(FILE));
    if (!f) {
        cs->arq_fechar(a);
        errno = ENOMEM;
        return NULL;
    }
    memset(f, 0, sizeof(*f));
    f->tipo = ARQ_DISCO;
    f->alca = a;
    f->modo = modo;
    f->devolvido = -1;
    f->fd = novo_fd(a, modo);
    if (f->fd >= 0) fds[f->fd].dono = f;
    return f;
}

FILE *fdopen(int fd, const char *m)
{
    if (!fd_valido(fd)) {
        errno = EBADF;
        return NULL;
    }
    if (fd == 1) return stdout;
    if (fd == 2) return stderr;
    if (fd == 0) return stdin;
    FILE *f = (FILE *)malloc(sizeof(FILE));
    if (!f) return NULL;
    memset(f, 0, sizeof(*f));
    f->tipo = ARQ_DISCO;
    f->alca = fds[fd].alca;
    f->modo = modo_de_texto(m ? m : "r");
    f->devolvido = -1;
    f->fd = fd;
    fds[fd].dono = f;
    return f;
}

FILE *freopen(const char *caminho, const char *m, FILE *f)
{
    if (f && f->tipo == ARQ_DISCO) fclose(f);
    return caminho ? fopen(caminho, m) : NULL;
}

static void descartar_buffer(FILE *f)
{
    if (f->buf && f->buf_pos < f->buf_len) {
        cs->arq_posicionar(f->alca, f->pos_buf + f->buf_pos, SEEK_SET);
        CS_CONFERIR();
    }
    f->buf_pos = f->buf_len = 0;
}

int fclose(FILE *f)
{
    if (!f) return EOF;
    if (f->tipo != ARQ_DISCO) return 0;
    cs->arq_fechar(f->alca);
    if (f->fd >= 3 && f->fd < MAX_FDS) {
        cs_girar(&trava_dos_fds);
        fds[f->fd].usado = 0;
        fds[f->fd].dono = NULL;
        cs_soltar(&trava_dos_fds);
    }
    free(f->buf);
    free(f);
    CS_CONFERIR();
    return 0;
}

static int encher(FILE *f)
{
    if (!f->buf) {
        f->buf = (unsigned char *)malloc(TAMANHO_DO_BUFFER);
        if (!f->buf) return 0;
    }
    f->pos_buf = cs->arq_posicionar(f->alca, 0, SEEK_CUR);
    int n = cs->arq_ler(f->alca, f->buf, TAMANHO_DO_BUFFER);
    CS_CONFERIR();
    if (n < 0) {
        f->erro = 1;
        n = 0;
    }
    f->buf_pos = 0;
    f->buf_len = n;
    if (n == 0) f->fim = 1;
    return n;
}

size_t fread(void *destino, size_t tamanho, size_t quantos, FILE *f)
{
    size_t total = tamanho * quantos;
    if (!f || total == 0) return 0;
    unsigned char *d = (unsigned char *)destino;
    size_t feito = 0;

    if (f->tipo == ARQ_ENTRADA) {
        while (feito < total) {
            int c = ler_da_serial();
            if (c < 0) break;
            d[feito++] = (unsigned char)c;
        }
        return feito / tamanho;
    }
    if (f->tipo != ARQ_DISCO || !(f->modo & CS_ARQ_LER)) {
        if (f) f->erro = 1;
        return 0;
    }

    if (f->devolvido >= 0 && feito < total) {
        d[feito++] = (unsigned char)f->devolvido;
        f->devolvido = -1;
    }
    while (feito < total) {
        int tem = f->buf_len - f->buf_pos;
        if (tem > 0) {
            size_t k = (size_t)tem < total - feito ? (size_t)tem : total - feito;
            memcpy(d + feito, f->buf + f->buf_pos, k);
            f->buf_pos += (int)k;
            feito += k;
            continue;
        }
        /* Pedido grande vai direto, sem passar pelo buffer. */
        if (total - feito >= TAMANHO_DO_BUFFER) {
            f->buf_pos = f->buf_len = 0;
            size_t falta = total - feito;
            int n = cs->arq_ler(f->alca, d + feito, falta > 0x40000000 ? 0x40000000 : (int)falta);
            CS_CONFERIR();
            if (n < 0) { f->erro = 1; break; }
            if (n == 0) { f->fim = 1; break; }
            feito += (size_t)n;
            continue;
        }
        if (encher(f) == 0) break;
    }
    return feito / tamanho;
}

size_t fwrite(const void *origem, size_t tamanho, size_t quantos, FILE *f)
{
    size_t total = tamanho * quantos;
    if (!f || total == 0) return 0;
    if (f->tipo == ARQ_SAIDA) {
        escrever_na_serial((const char *)origem, (int)total);
        return quantos;
    }
    if (f->tipo != ARQ_DISCO || !(f->modo & CS_ARQ_ESCREVER)) {
        f->erro = 1;
        errno = EBADF;
        return 0;
    }
    descartar_buffer(f);
    f->devolvido = -1;
    int n = cs->arq_escrever(f->alca, origem, (int)total);
    CS_CONFERIR();
    if (n < 0) {
        f->erro = 1;
        errno = -n;
        return 0;
    }
    return (size_t)n / tamanho;
}

int fseek(FILE *f, long desloc, int de_onde)
{
    if (!f || f->tipo != ARQ_DISCO) {
        errno = ESPIPE;
        return -1;
    }
    int64_t alvo = desloc;
    if (de_onde == SEEK_CUR) {
        alvo += ftell(f);
        de_onde = SEEK_SET;
    }
    f->buf_pos = f->buf_len = 0;
    f->devolvido = -1;
    int64_t r = cs->arq_posicionar(f->alca, alvo, de_onde);
    CS_CONFERIR();
    if (r < 0) {
        errno = EINVAL;
        return -1;
    }
    f->fim = 0;
    return 0;
}

long ftell(FILE *f)
{
    if (!f || f->tipo != ARQ_DISCO) {
        errno = ESPIPE;
        return -1;
    }
    int64_t pos;
    if (f->buf_len > 0) pos = f->pos_buf + f->buf_pos;
    else pos = cs->arq_posicionar(f->alca, 0, SEEK_CUR);
    if (f->devolvido >= 0) pos--;
    return (long)pos;
}

void rewind(FILE *f)
{
    fseek(f, 0, SEEK_SET);
    if (f) f->erro = 0;
}

int fgetpos(FILE *f, fpos_t *pos)
{
    long p = ftell(f);
    if (p < 0) return -1;
    *pos = p;
    return 0;
}

int fsetpos(FILE *f, const fpos_t *pos)
{
    return fseek(f, (long)*pos, SEEK_SET);
}

int feof(FILE *f) { return f ? f->fim && f->buf_pos >= f->buf_len && f->devolvido < 0 : 0; }
int ferror(FILE *f) { return f ? f->erro : 0; }

void clearerr(FILE *f)
{
    if (f) f->fim = f->erro = 0;
}

int fflush(FILE *f)
{
    (void)f;
    return 0;
}

int fileno(FILE *f)
{
    return f ? f->fd : -1;
}

int setvbuf(FILE *f, char *buf, int modo, size_t tamanho)
{
    (void)f; (void)buf; (void)modo; (void)tamanho;
    return 0;
}

void setbuf(FILE *f, char *buf)
{
    (void)f; (void)buf;
}

int fgetc(FILE *f)
{
    if (!f) return EOF;
    if (f->tipo == ARQ_ENTRADA) {
        if (f->devolvido >= 0) {
            int c = f->devolvido;
            f->devolvido = -1;
            return c;
        }
        int c = ler_da_serial();
        return c < 0 ? EOF : c;
    }
    if (f->devolvido >= 0) {
        int c = f->devolvido;
        f->devolvido = -1;
        return c;
    }
    if (f->tipo != ARQ_DISCO || !(f->modo & CS_ARQ_LER)) return EOF;
    if (f->buf_pos >= f->buf_len && encher(f) == 0) return EOF;
    return f->buf[f->buf_pos++];
}

int getc(FILE *f) { return fgetc(f); }
int getchar(void) { return fgetc(stdin); }

int ungetc(int c, FILE *f)
{
    if (!f || c == EOF) return EOF;
    f->devolvido = c & 0xff;
    f->fim = 0;
    return c;
}

char *fgets(char *s, int n, FILE *f)
{
    if (n <= 0) return NULL;
    int i = 0;
    while (i < n - 1) {
        int c = fgetc(f);
        if (c == EOF) break;
        s[i++] = (char)c;
        if (c == '\n') break;
    }
    if (i == 0) return NULL;
    s[i] = 0;
    return s;
}

ssize_t getline(char **linha, size_t *n, FILE *f)
{
    if (!*linha || *n == 0) {
        *n = 128;
        *linha = (char *)malloc(*n);
        if (!*linha) return -1;
    }
    size_t i = 0;
    for (;;) {
        int c = fgetc(f);
        if (c == EOF) break;
        if (i + 2 > *n) {
            char *maior = (char *)realloc(*linha, *n * 2);
            if (!maior) return -1;
            *linha = maior;
            *n *= 2;
        }
        (*linha)[i++] = (char)c;
        if (c == '\n') break;
    }
    if (i == 0) return -1;
    (*linha)[i] = 0;
    return (ssize_t)i;
}

int fputc(int c, FILE *f)
{
    unsigned char b = (unsigned char)c;
    return fwrite(&b, 1, 1, f) == 1 ? b : EOF;
}

int putc(int c, FILE *f) { return fputc(c, f); }
int putchar(int c) { return fputc(c, stdout); }

int fputs(const char *s, FILE *f)
{
    size_t n = strlen(s);
    return fwrite(s, 1, n, f) == n ? 1 : EOF;
}

int puts(const char *s)
{
    size_t n = strlen(s);
    char pilha[256];
    if (n + 1 < sizeof(pilha)) {
        memcpy(pilha, s, n);
        pilha[n] = '\n';
        escrever_na_serial(pilha, (int)n + 1);
    } else {
        escrever_na_serial(s, (int)n);
        escrever_na_serial("\n", 1);
    }
    return 1;
}

int cs_vformatar_para(void (*escrever)(void *, const char *, int), void *alvo, const char *formato, va_list ap);

static void para_o_arquivo(void *alvo, const char *p, int n)
{
    fwrite(p, 1, (size_t)n, (FILE *)alvo);
}

int vfprintf(FILE *f, const char *formato, va_list ap)
{
    return cs_vformatar_para(para_o_arquivo, f, formato, ap);
}

int fprintf(FILE *f, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vfprintf(f, formato, ap);
    va_end(ap);
    return r;
}

int vprintf(const char *formato, va_list ap)
{
    return vfprintf(stdout, formato, ap);
}

int printf(const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vfprintf(stdout, formato, ap);
    va_end(ap);
    return r;
}

int vsscanf(const char *s, const char *formato, va_list ap);

int fscanf(FILE *f, const char *formato, ...)
{
    /* Le uma linha e interpreta: basta para os arquivos de configuracao. */
    char linha[512];
    if (!fgets(linha, sizeof(linha), f)) return EOF;
    va_list ap;
    va_start(ap, formato);
    int r = vsscanf(linha, formato, ap);
    va_end(ap);
    return r;
}

int scanf(const char *formato, ...)
{
    char linha[256];
    if (!fgets(linha, sizeof(linha), stdin)) return EOF;
    va_list ap;
    va_start(ap, formato);
    int r = vsscanf(linha, formato, ap);
    va_end(ap);
    return r;
}

void perror(const char *s)
{
    if (s && *s) fprintf(stderr, "%s: %s\n", s, strerror(errno));
    else fprintf(stderr, "%s\n", strerror(errno));
}

int remove(const char *caminho)
{
    int r = cs->arq_apagar(caminho);
    CS_CONFERIR();
    if (r < 0) {
        errno = -r;
        return -1;
    }
    return 0;
}

int rename(const char *velho, const char *novo)
{
    int r = cs->arq_renomear(velho, novo);
    CS_CONFERIR();
    if (r < 0) {
        errno = -r;
        return -1;
    }
    return 0;
}

FILE *tmpfile(void)
{
    errno = ENOSYS;
    return NULL;
}

char *tmpnam(char *s)
{
    (void)s;
    return NULL;
}

/* ---------------------------------------------------------------------
 * POSIX */
int open(const char *caminho, int flags, ...)
{
    int modo = modo_de_flags(flags);
    int64_t a = abrir_no_simulador(caminho, modo);
    if (a < 0) return -1;
    int fd = novo_fd(a, modo);
    if (fd < 0) {
        cs->arq_fechar(a);
        errno = EMFILE;
        return -1;
    }
    return fd;
}

int close(int fd)
{
    if (!fd_valido(fd)) {
        errno = EBADF;
        return -1;
    }
    if (fd < 3) return 0;
    if (fds[fd].dono) return fclose(fds[fd].dono);
    cs->arq_fechar(fds[fd].alca);
    cs_girar(&trava_dos_fds);
    fds[fd].usado = 0;
    cs_soltar(&trava_dos_fds);
    CS_CONFERIR();
    return 0;
}

ssize_t read(int fd, void *buf, size_t n)
{
    if (fd == 0) {
        unsigned char *d = (unsigned char *)buf;
        size_t k = 0;
        while (k < n) {
            int c = ler_da_serial();
            if (c < 0) break;
            d[k++] = (unsigned char)c;
        }
        if (k == 0) {
            errno = EAGAIN;
            return -1;
        }
        return (ssize_t)k;
    }
    if (!fd_valido(fd) || fd < 3) {
        errno = EBADF;
        return -1;
    }
    if (fds[fd].dono) return (ssize_t)fread(buf, 1, n, fds[fd].dono);
    int r = cs->arq_ler(fds[fd].alca, buf, n > 0x40000000 ? 0x40000000 : (int)n);
    CS_CONFERIR();
    if (r < 0) {
        errno = -r;
        return -1;
    }
    return r;
}

ssize_t write(int fd, const void *buf, size_t n)
{
    if (fd == 1 || fd == 2) {
        escrever_na_serial((const char *)buf, (int)n);
        return (ssize_t)n;
    }
    if (!fd_valido(fd) || fd < 3) {
        errno = EBADF;
        return -1;
    }
    if (fds[fd].dono) return (ssize_t)fwrite(buf, 1, n, fds[fd].dono);
    int r = cs->arq_escrever(fds[fd].alca, buf, (int)n);
    CS_CONFERIR();
    if (r < 0) {
        errno = -r;
        return -1;
    }
    return r;
}

off_t lseek(int fd, off_t desloc, int de_onde)
{
    if (!fd_valido(fd) || fd < 3) {
        errno = ESPIPE;
        return -1;
    }
    if (fds[fd].dono) {
        if (fseek(fds[fd].dono, desloc, de_onde) < 0) return -1;
        return ftell(fds[fd].dono);
    }
    int64_t r = cs->arq_posicionar(fds[fd].alca, desloc, de_onde);
    CS_CONFERIR();
    if (r < 0) {
        errno = EINVAL;
        return -1;
    }
    return (off_t)r;
}

ssize_t pread(int fd, void *buf, size_t n, off_t desloc)
{
    off_t antes = lseek(fd, 0, SEEK_CUR);
    if (antes < 0 || lseek(fd, desloc, SEEK_SET) < 0) return -1;
    ssize_t r = read(fd, buf, n);
    lseek(fd, antes, SEEK_SET);
    return r;
}

ssize_t pwrite(int fd, const void *buf, size_t n, off_t desloc)
{
    off_t antes = lseek(fd, 0, SEEK_CUR);
    if (antes < 0 || lseek(fd, desloc, SEEK_SET) < 0) return -1;
    ssize_t r = write(fd, buf, n);
    lseek(fd, antes, SEEK_SET);
    return r;
}

int stat(const char *caminho, struct stat *st)
{
    int64_t tamanho = 0;
    int tipo = cs->arq_info(caminho, &tamanho);
    CS_CONFERIR();
    if (tipo == 0) {
        errno = ENOENT;
        return -1;
    }
    memset(st, 0, sizeof(*st));
    st->st_mode = tipo == 2 ? (S_IFDIR | 0777) : (S_IFREG | 0666);
    st->st_size = (off_t)tamanho;
    st->st_blksize = 512;
    st->st_blocks = (blkcnt_t)((tamanho + 511) / 512);
    st->st_nlink = 1;
    return 0;
}

int fstat(int fd, struct stat *st)
{
    memset(st, 0, sizeof(*st));
    if (fd >= 0 && fd <= 2) {
        st->st_mode = 0020000;
        return 0;
    }
    if (!fd_valido(fd)) {
        errno = EBADF;
        return -1;
    }
    int64_t tamanho = cs->arq_tamanho(fds[fd].alca);
    CS_CONFERIR();
    st->st_mode = S_IFREG | 0666;
    st->st_size = (off_t)tamanho;
    st->st_blksize = 512;
    st->st_nlink = 1;
    return 0;
}

int lstat(const char *caminho, struct stat *st)
{
    return stat(caminho, st);
}

int access(const char *caminho, int modo)
{
    (void)modo;
    int64_t tamanho;
    int tipo = cs->arq_info(caminho, &tamanho);
    CS_CONFERIR();
    if (tipo == 0) {
        errno = ENOENT;
        return -1;
    }
    return 0;
}

int unlink(const char *caminho) { return remove(caminho); }
int rmdir(const char *caminho) { return remove(caminho); }

int mkdir(const char *caminho, mode_t modo)
{
    (void)modo;
    int r = cs->arq_criar_pasta(caminho);
    CS_CONFERIR();
    if (r < 0) {
        errno = -r;
        return -1;
    }
    return 0;
}

int chdir(const char *caminho)
{
    (void)caminho;
    errno = ENOSYS;
    return -1;
}

char *getcwd(char *buf, size_t n)
{
    if (!buf || n < 2) {
        errno = ERANGE;
        return NULL;
    }
    strcpy(buf, "/");
    return buf;
}

int fsync(int fd) { return fd_valido(fd) ? 0 : -1; }

int ftruncate(int fd, off_t n)
{
    (void)fd; (void)n;
    errno = ENOSYS;
    return -1;
}

int truncate(const char *caminho, off_t n)
{
    (void)caminho; (void)n;
    errno = ENOSYS;
    return -1;
}

int isatty(int fd) { return fd >= 0 && fd <= 2; }
int dup(int fd) { errno = ENOSYS; (void)fd; return -1; }
int dup2(int fd, int novo) { errno = ENOSYS; (void)fd; (void)novo; return -1; }
int link(const char *velho, const char *novo) { (void)velho; (void)novo; errno = ENOSYS; return -1; }

int fcntl(int fd, int comando, ...)
{
    (void)comando;
    return fd_valido(fd) ? 0 : -1;
}

long sysconf(int nome)
{
    (void)nome;
    return -1;
}

/* ---------------------------------------------------------------------
 * pastas */
struct cs_pasta {
    int64_t alca;
    struct dirent atual;
    long pos;
    char caminho[256];
};

DIR *opendir(const char *caminho)
{
    int64_t p = cs->pasta_abrir(caminho);
    CS_CONFERIR();
    if (p <= 0) {
        errno = ENOENT;
        return NULL;
    }
    DIR *d = (DIR *)malloc(sizeof(DIR));
    if (!d) {
        cs->pasta_fechar(p);
        return NULL;
    }
    memset(d, 0, sizeof(*d));
    d->alca = p;
    strncpy(d->caminho, caminho, sizeof(d->caminho) - 1);
    return d;
}

struct dirent *readdir(DIR *d)
{
    if (!d) return NULL;
    int tipo = 0;
    int r = cs->pasta_ler(d->alca, d->atual.d_name, (int)sizeof(d->atual.d_name), &tipo);
    CS_CONFERIR();
    if (r <= 0) return NULL;
    d->atual.d_type = tipo == 2 ? DT_DIR : DT_REG;
    d->atual.d_ino = (ino_t)(++d->pos);
    return &d->atual;
}

int closedir(DIR *d)
{
    if (!d) return -1;
    cs->pasta_fechar(d->alca);
    free(d);
    return 0;
}

void rewinddir(DIR *d)
{
    if (!d) return;
    cs->pasta_fechar(d->alca);
    d->alca = cs->pasta_abrir(d->caminho);
    d->pos = 0;
}

long telldir(DIR *d) { return d ? d->pos : -1; }

void seekdir(DIR *d, long pos)
{
    if (!d) return;
    rewinddir(d);
    while (d->pos < pos && readdir(d)) {}
}

/* ---------------------------------------------------------------------
 * particoes e flash */
typedef struct particao_viva {
    esp_partition_t p;
    struct particao_viva *proxima;
} particao_viva;

static particao_viva *particoes;
static cs_giro trava_das_particoes;

static const esp_partition_t *particao_do_simulador(int tipo, int subtipo, const char *rotulo, int indice)
{
    int64_t endereco = 0, tamanho = 0;
    int tipo_achado = 0, subtipo_achado = 0;
    char achado[20];
    memset(achado, 0, sizeof(achado));
    int64_t id = cs->particao_achar(tipo, subtipo, rotulo, indice, &endereco, &tamanho, achado,
                                    &tipo_achado, &subtipo_achado);
    CS_CONFERIR();
    if (id <= 0) return NULL;

    cs_girar(&trava_das_particoes);
    for (particao_viva *v = particoes; v; v = v->proxima)
        if (v->p.__cs_particao == id) {
            cs_soltar(&trava_das_particoes);
            return &v->p;
        }
    cs_soltar(&trava_das_particoes);

    particao_viva *v = (particao_viva *)malloc(sizeof(particao_viva));
    if (!v) return NULL;
    memset(v, 0, sizeof(*v));
    v->p.flash_chip = NULL;
    v->p.type = (esp_partition_type_t)tipo_achado;
    v->p.subtype = (esp_partition_subtype_t)subtipo_achado;
    v->p.address = (uint32_t)endereco;
    v->p.size = (uint32_t)tamanho;
    v->p.erase_size = 4096;
    strncpy(v->p.label, achado, sizeof(v->p.label) - 1);
    v->p.__cs_particao = id;
    cs_girar(&trava_das_particoes);
    v->proxima = particoes;
    particoes = v;
    cs_soltar(&trava_das_particoes);
    return &v->p;
}

const esp_partition_t *esp_partition_find_first(esp_partition_type_t tipo, esp_partition_subtype_t subtipo,
                                                const char *rotulo)
{
    return particao_do_simulador(tipo, subtipo, rotulo, 0);
}

struct cs_iterador_particao {
    int tipo, subtipo, indice;
    char rotulo[20];
    int tem_rotulo;
    const esp_partition_t *atual;
};

esp_partition_iterator_t esp_partition_find(esp_partition_type_t tipo, esp_partition_subtype_t subtipo,
                                            const char *rotulo)
{
    const esp_partition_t *p = particao_do_simulador(tipo, subtipo, rotulo, 0);
    if (!p) return NULL;
    esp_partition_iterator_t it = (esp_partition_iterator_t)malloc(sizeof(struct cs_iterador_particao));
    if (!it) return NULL;
    memset(it, 0, sizeof(*it));
    it->tipo = tipo;
    it->subtipo = subtipo;
    if (rotulo) {
        strncpy(it->rotulo, rotulo, sizeof(it->rotulo) - 1);
        it->tem_rotulo = 1;
    }
    it->atual = p;
    return it;
}

const esp_partition_t *esp_partition_get(esp_partition_iterator_t it)
{
    return it ? it->atual : NULL;
}

esp_partition_iterator_t esp_partition_next(esp_partition_iterator_t it)
{
    if (!it) return NULL;
    it->indice++;
    it->atual = particao_do_simulador(it->tipo, it->subtipo, it->tem_rotulo ? it->rotulo : NULL, it->indice);
    if (!it->atual) {
        free(it);
        return NULL;
    }
    return it;
}

void esp_partition_iterator_release(esp_partition_iterator_t it)
{
    free(it);
}

esp_err_t esp_partition_read(const esp_partition_t *p, size_t desloc, void *buf, size_t n)
{
    if (!p || !buf) return ESP_ERR_INVALID_ARG;
    if (desloc + n > p->size) return ESP_ERR_INVALID_SIZE;
    int r = cs->particao_ler(p->__cs_particao, (int64_t)desloc, buf, (int64_t)n);
    CS_CONFERIR();
    return r;
}

esp_err_t esp_partition_read_raw(const esp_partition_t *p, size_t desloc, void *buf, size_t n)
{
    return esp_partition_read(p, desloc, buf, n);
}

esp_err_t esp_partition_write(const esp_partition_t *p, size_t desloc, const void *buf, size_t n)
{
    if (!p || !buf) return ESP_ERR_INVALID_ARG;
    if (desloc + n > p->size) return ESP_ERR_INVALID_SIZE;
    int r = cs->particao_escrever(p->__cs_particao, (int64_t)desloc, buf, (int64_t)n);
    CS_CONFERIR();
    return r;
}

esp_err_t esp_partition_erase_range(const esp_partition_t *p, size_t desloc, size_t n)
{
    if (!p) return ESP_ERR_INVALID_ARG;
    if ((desloc % 4096) || (n % 4096)) return ESP_ERR_INVALID_SIZE;
    if (desloc + n > p->size) return ESP_ERR_INVALID_SIZE;
    int r = cs->particao_apagar(p->__cs_particao, (int64_t)desloc, (int64_t)n);
    CS_CONFERIR();
    return r;
}

/* O mmap da flash vira uma copia: no PC nao ha a janela da cache. */
typedef struct mapa_vivo { uint32_t alca; void *memoria; struct mapa_vivo *proximo; } mapa_vivo;
static mapa_vivo *mapas;
static uint32_t proximo_mapa = 1;
static cs_giro trava_dos_mapas;

static esp_err_t mapear(int64_t particao, size_t desloc, size_t n, const void **ponteiro, uint32_t *alca)
{
    void *m = heap_caps_malloc(n ? n : 1, MALLOC_CAP_SPIRAM);
    if (!m) m = malloc(n ? n : 1);
    if (!m) return ESP_ERR_NO_MEM;
    int r = particao >= 0 ? cs->particao_ler(particao, (int64_t)desloc, m, (int64_t)n)
                          : cs->flash_ler((int64_t)desloc, m, (int64_t)n);
    CS_CONFERIR();
    if (r != ESP_OK) {
        free(m);
        return r;
    }
    mapa_vivo *v = (mapa_vivo *)malloc(sizeof(mapa_vivo));
    if (!v) {
        free(m);
        return ESP_ERR_NO_MEM;
    }
    cs_girar(&trava_dos_mapas);
    v->alca = proximo_mapa++;
    v->memoria = m;
    v->proximo = mapas;
    mapas = v;
    cs_soltar(&trava_dos_mapas);
    *ponteiro = m;
    *alca = v->alca;
    return ESP_OK;
}

static void desmapear(uint32_t alca)
{
    cs_girar(&trava_dos_mapas);
    for (mapa_vivo **p = &mapas; *p; p = &(*p)->proximo)
        if ((*p)->alca == alca) {
            mapa_vivo *v = *p;
            *p = v->proximo;
            cs_soltar(&trava_dos_mapas);
            free(v->memoria);
            free(v);
            return;
        }
    cs_soltar(&trava_dos_mapas);
}

esp_err_t esp_partition_mmap(const esp_partition_t *p, size_t desloc, size_t n,
                             esp_partition_mmap_memory_t memoria, const void **ponteiro,
                             esp_partition_mmap_handle_t *alca)
{
    (void)memoria;
    if (!p) return ESP_ERR_INVALID_ARG;
    if (desloc + n > p->size) return ESP_ERR_INVALID_SIZE;
    return mapear(p->__cs_particao, desloc, n, ponteiro, alca);
}

void esp_partition_munmap(esp_partition_mmap_handle_t alca) { desmapear(alca); }

esp_err_t spi_flash_mmap(size_t endereco, size_t n, spi_flash_mmap_memory_t memoria, const void **ponteiro,
                         spi_flash_mmap_handle_t *alca)
{
    (void)memoria;
    return mapear(-1, endereco, n, ponteiro, alca);
}

void spi_flash_munmap(spi_flash_mmap_handle_t alca) { desmapear(alca); }

esp_flash_t *esp_flash_default_chip;

esp_err_t esp_flash_read(esp_flash_t *chip, void *buf, uint32_t endereco, uint32_t n)
{
    (void)chip;
    int r = cs->flash_ler((int64_t)endereco, buf, (int64_t)n);
    CS_CONFERIR();
    return r;
}

esp_err_t esp_flash_write(esp_flash_t *chip, const void *buf, uint32_t endereco, uint32_t n)
{
    (void)chip; (void)buf; (void)endereco; (void)n;
    return ESP_ERR_NOT_SUPPORTED;
}

esp_err_t esp_flash_erase_region(esp_flash_t *chip, uint32_t endereco, uint32_t n)
{
    (void)chip; (void)endereco; (void)n;
    return ESP_ERR_NOT_SUPPORTED;
}

esp_err_t esp_flash_get_size(esp_flash_t *chip, uint32_t *tamanho)
{
    (void)chip;
    *tamanho = 8 * 1024 * 1024;
    return ESP_OK;
}
