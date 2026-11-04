/* cs_idf_vfs.cpp -- os arquivos do jeito do ESP-IDF: fopen("/spiffs/a.txt"), open,
 * stat, opendir, depois de esp_vfs_spiffs_register ou esp_vfs_fat_*_mount. Os
 * arquivos moram nos sistemas de arquivos da placa (o SPIFFS, o FFat e o SD, os
 * mesmos do Arduino). Cada arquivo aberto vira uma copia temporaria no computador:
 * o fread, o fprintf e os outros do C trabalham nela, e o fclose devolve para a
 * placa. Fora dos pontos de montagem, o caminho e o de sempre. */
#define CS_VFS_INTERNO 1
#include "Arduino.h"
#if defined(ESP32)
#include "SPIFFS.h"
#include "FFat.h"
#include "SD.h"
#include "esp_spiffs.h"
#include "esp_vfs_fat.h"
#include "sdmmc_cmd.h"
#include <direct.h>
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <io.h>
#include <stdio.h>
#include <sys/stat.h>
#include <map>
#include <string>
#include <vector>

extern "C" {
__declspec(dllimport) unsigned long __stdcall GetTempPathA(unsigned long n, char *b);
__declspec(dllimport) unsigned long __stdcall GetCurrentProcessId(void);
__declspec(dllimport) void *__stdcall GetModuleHandleA(const char *nome);
__declspec(dllimport) void *__stdcall GetProcAddress(void *modulo, const char *nome);
__declspec(dllimport) int __stdcall DeleteFileA(const char *nome);
__declspec(dllimport) int __stdcall MoveFileA(const char *de, const char *para);
}

/* O fopen e o fclose de verdade: os nomes daqui ficam no lugar deles dentro do sketch. */
static void *doC(const char *nome)
{
    void *m = GetModuleHandleA("api-ms-win-crt-stdio-l1-1-0.dll");
    if (!m) m = GetModuleHandleA("ucrtbase.dll");
    return m ? GetProcAddress(m, nome) : nullptr;
}
static FILE *fopenDoC(const char *caminho, const char *modo)
{
    static auto f = (FILE * (*)(const char *, const char *)) doC("fopen");
    return f(caminho, modo);
}
static int fcloseDoC(FILE *a)
{
    static auto f = (int (*)(FILE *))doC("fclose");
    return f(a);
}

namespace {
enum Tipo { T_SPIFFS, T_FAT, T_SD };
struct Montagem {
    std::string base, rotulo;
    fs::FS *fs;
    Tipo tipo;
};
std::vector<Montagem> &montagens()
{
    static auto *v = new std::vector<Montagem>();
    return *v;
}
Montagem *acharMontagem(const char *caminho, std::string *dentro = nullptr)
{
    if (!caminho) return nullptr;
    for (Montagem &m : montagens()) {
        size_t n = m.base.size();
        if (strncmp(caminho, m.base.c_str(), n) == 0 && (caminho[n] == '/' || caminho[n] == 0)) {
            if (dentro) *dentro = caminho[n] ? caminho + n : "/";
            return &m;
        }
    }
    return nullptr;
}
struct Aberto {
    fs::FS *fs;
    std::string dentro, temporario;
    bool escreve;
};
std::map<FILE *, Aberto> &arquivosAbertos()
{
    static auto *v = new std::map<FILE *, Aberto>();
    return *v;
}
std::map<int, Aberto> &descritoresAbertos()
{
    static auto *v = new std::map<int, Aberto>();
    return *v;
}
std::string nomeTemporario()
{
    static int n = 0;
    char pasta[260], nome[300];
    unsigned long k = GetTempPathA(sizeof(pasta), pasta);
    if (!k) strcpy(pasta, ".\\");
    snprintf(nome, sizeof(nome), "%scs_vfs_%lu_%d.tmp", pasta, GetCurrentProcessId(), ++n);
    return nome;
}
void trazerDaPlaca(fs::FS *fs, const std::string &dentro, const std::string &temporario, bool trazer)
{
    FILE *f = fopenDoC(temporario.c_str(), "wb");
    if (!f) return;
    if (trazer) {
        File a = fs->open(dentro.c_str(), "r");
        if (a) {
            static uint8_t pedaco[4096];
            int n;
            while ((n = a.read(pedaco, sizeof(pedaco))) > 0) fwrite(pedaco, 1, (size_t)n, f);
            a.close();
        }
    }
    fcloseDoC(f);
}
void levarParaPlaca(fs::FS *fs, const std::string &dentro, const std::string &temporario)
{
    FILE *f = fopenDoC(temporario.c_str(), "rb");
    if (!f) return;
    File a = fs->open(dentro.c_str(), "w", true);
    if (a) {
        static uint8_t pedaco[4096];
        size_t n;
        while ((n = fread(pedaco, 1, sizeof(pedaco), f)) > 0) a.write(pedaco, n);
        a.close();
    }
    fcloseDoC(f);
}
bool arquivoExiste(fs::FS *fs, const std::string &dentro)
{
    if (!fs->exists(dentro.c_str())) return false;
    File a = fs->open(dentro.c_str(), "r");
    bool arquivo = a && !a.isDirectory();
    if (a) a.close();
    return arquivo;
}
} /* namespace */

/* =====================================================================
 * o stdio */
extern "C" {

FILE *fopen(const char *caminho, const char *modo)
{
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return fopenDoC(caminho, modo);
    bool le = modo[0] == 'r', acrescenta = modo[0] == 'a', mais = strchr(modo, '+') != nullptr;
    bool existe = arquivoExiste(m->fs, dentro);
    if (le && !existe) {
        errno = ENOENT;
        return nullptr;
    }
    std::string temporario = nomeTemporario();
    trazerDaPlaca(m->fs, dentro, temporario, (le || acrescenta) && existe);
    /* Sempre binario: a placa nao troca fim de linha. */
    char modoDoC[8];
    snprintf(modoDoC, sizeof(modoDoC), "%c%sb", modo[0], mais ? "+" : "");
    FILE *f = fopenDoC(temporario.c_str(), modoDoC);
    if (!f) {
        errno = EIO;
        return nullptr;
    }
    arquivosAbertos()[f] = {m->fs, dentro, temporario, !le || mais};
    return f;
}
int fclose(FILE *f)
{
    auto i = arquivosAbertos().find(f);
    if (i == arquivosAbertos().end()) return fcloseDoC(f);
    Aberto a = i->second;
    arquivosAbertos().erase(i);
    int r = fcloseDoC(f);
    if (a.escreve) levarParaPlaca(a.fs, a.dentro, a.temporario);
    DeleteFileA(a.temporario.c_str());
    return r;
}
int remove(const char *caminho)
{
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return DeleteFileA(caminho) ? 0 : (errno = ENOENT, -1);
    if (!m->fs->exists(dentro.c_str())) {
        errno = ENOENT;
        return -1;
    }
    return m->fs->remove(dentro.c_str()) ? 0 : (errno = EIO, -1);
}
int unlink(const char *caminho) { return remove(caminho); }
int rename(const char *de, const char *para)
{
    std::string a, b;
    Montagem *m = acharMontagem(de, &a), *n = acharMontagem(para, &b);
    if (!m && !n) return MoveFileA(de, para) ? 0 : (errno = ENOENT, -1);
    if (m != n) {
        errno = EXDEV;
        return -1;
    }
    if (!m->fs->exists(a.c_str())) {
        errno = ENOENT;
        return -1;
    }
    if (m->tipo == T_FAT && m->fs->exists(b.c_str())) {
        errno = EEXIST;
        return -1;
    }
    return m->fs->rename(a.c_str(), b.c_str()) ? 0 : (errno = EIO, -1);
}

/* ---- o POSIX ---- */
int open(const char *caminho, int flags, ...)
{
    va_list ap;
    va_start(ap, flags);
    int modo = va_arg(ap, int);
    va_end(ap);
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return _open(caminho, flags, modo);
    bool existe = arquivoExiste(m->fs, dentro);
    if (!existe && !(flags & O_CREAT)) {
        errno = ENOENT;
        return -1;
    }
    if (existe && (flags & O_CREAT) && (flags & O_EXCL)) {
        errno = EEXIST;
        return -1;
    }
    std::string temporario = nomeTemporario();
    trazerDaPlaca(m->fs, dentro, temporario, existe && !(flags & O_TRUNC));
    int fd = _open(temporario.c_str(), (flags & ~(O_CREAT | O_EXCL | O_TRUNC)) | O_BINARY, 0666);
    if (fd < 0) {
        errno = EIO;
        return -1;
    }
    descritoresAbertos()[fd] = {m->fs, dentro, temporario, (flags & (O_WRONLY | O_RDWR)) != 0};
    return fd;
}
/* Chamado pelo close (cs_sockets.cpp) para o descritor que nao e socket. */
int cs_vfs_fechar(int fd)
{
    auto i = descritoresAbertos().find(fd);
    if (i == descritoresAbertos().end()) return _close(fd);
    Aberto a = i->second;
    descritoresAbertos().erase(i);
    int r = _close(fd);
    if (a.escreve) levarParaPlaca(a.fs, a.dentro, a.temporario);
    DeleteFileA(a.temporario.c_str());
    return r;
}

/* O stat do programa (sys/stat.h do simulador): os campos que a placa preenche. */
int cs_vfs_stat(const char *caminho, struct stat *st)
{
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return stat(caminho, st);
    if (!m->fs->exists(dentro.c_str()) && dentro != "/") {
        errno = ENOENT;
        return -1;
    }
    memset(st, 0, sizeof(*st));
    File a = m->fs->open(dentro.c_str(), "r");
    bool pasta = dentro == "/" || (a && a.isDirectory());
    st->st_mode = (unsigned short)(pasta ? _S_IFDIR : _S_IFREG);
    if (a) {
        st->st_size = (long)a.size();
        a.close();
    }
    return 0;
}
int cs_vfs_mkdir(const char *caminho, int modo)
{
    (void)modo;
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return _mkdir(caminho);
    /* O SPIFFS nao tem pastas. */
    if (m->tipo == T_SPIFFS) {
        errno = ENOTSUP;
        return -1;
    }
    if (m->fs->exists(dentro.c_str())) {
        errno = EEXIST;
        return -1;
    }
    return m->fs->mkdir(dentro.c_str()) ? 0 : (errno = EIO, -1);
}
int cs_vfs_rmdir(const char *caminho)
{
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) return _rmdir(caminho);
    if (m->tipo == T_SPIFFS) {
        errno = ENOTSUP;
        return -1;
    }
    return m->fs->rmdir(dentro.c_str()) ? 0 : (errno = ENOENT, -1);
}

/* ---- as pastas (o dirent.h do simulador) ---- */
struct cs_dir {
    std::vector<std::string> nomes;
    std::vector<int> tipos;
    size_t pos;
    struct cs_dirent {
        int d_ino;
        unsigned char d_type;
        char d_name[256];
    } atual;
};
void *cs_vfs_opendir(const char *caminho)
{
    std::string dentro;
    Montagem *m = acharMontagem(caminho, &dentro);
    if (!m) {
        errno = ENOENT;
        return nullptr;
    }
    File d = m->fs->open(dentro.c_str(), "r");
    if (!d || !d.isDirectory()) {
        errno = ENOENT;
        return nullptr;
    }
    auto *r = new cs_dir();
    r->pos = 0;
    for (File f = d.openNextFile(); f; f = d.openNextFile()) {
        const char *n = f.name();
        const char *barra = strrchr(n, '/');
        r->nomes.push_back(barra ? barra + 1 : n);
        r->tipos.push_back(f.isDirectory() ? 2 : 1);
        f.close();
    }
    d.close();
    return r;
}
void *cs_vfs_readdir(void *dir)
{
    auto *r = (cs_dir *)dir;
    if (!r || r->pos >= r->nomes.size()) return nullptr;
    r->atual.d_ino = (int)r->pos;
    r->atual.d_type = (unsigned char)r->tipos[r->pos];
    snprintf(r->atual.d_name, sizeof(r->atual.d_name), "%s", r->nomes[r->pos].c_str());
    r->pos++;
    return &r->atual;
}
int cs_vfs_closedir(void *dir)
{
    delete (cs_dir *)dir;
    return 0;
}
void cs_vfs_rewinddir(void *dir)
{
    if (dir) ((cs_dir *)dir)->pos = 0;
}

/* =====================================================================
 * o SPIFFS do IDF */
esp_err_t esp_vfs_spiffs_register(const esp_vfs_spiffs_conf_t *conf)
{
    if (!conf || !conf->base_path) return ESP_ERR_INVALID_ARG;
    if (acharMontagem(conf->base_path)) return ESP_ERR_INVALID_STATE;
    if (!SPIFFS.begin(conf->format_if_mount_failed, conf->base_path)) return ESP_FAIL;
    montagens().push_back({conf->base_path, conf->partition_label ? conf->partition_label : "", &SPIFFS, T_SPIFFS});
    return ESP_OK;
}
static Montagem *spiffsDoRotulo(const char *rotulo)
{
    for (Montagem &m : montagens())
        if (m.tipo == T_SPIFFS && (!rotulo || m.rotulo == rotulo)) return &m;
    return nullptr;
}
esp_err_t esp_vfs_spiffs_unregister(const char *rotulo)
{
    auto &v = montagens();
    for (size_t i = 0; i < v.size(); i++)
        if (v[i].tipo == T_SPIFFS && (!rotulo || v[i].rotulo == rotulo)) {
            SPIFFS.end();
            v.erase(v.begin() + (long)i);
            return ESP_OK;
        }
    return ESP_ERR_INVALID_STATE;
}
bool esp_spiffs_mounted(const char *rotulo) { return spiffsDoRotulo(rotulo) != nullptr; }
esp_err_t esp_spiffs_format(const char *rotulo)
{
    (void)rotulo;
    return SPIFFS.format() ? ESP_OK : ESP_FAIL;
}
esp_err_t esp_spiffs_info(const char *rotulo, size_t *total, size_t *usado)
{
    if (!spiffsDoRotulo(rotulo)) return ESP_ERR_INVALID_STATE;
    if (total) *total = SPIFFS.totalBytes();
    if (usado) *usado = SPIFFS.usedBytes();
    return ESP_OK;
}
esp_err_t esp_spiffs_check(const char *rotulo) { return spiffsDoRotulo(rotulo) ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t esp_spiffs_gc(const char *rotulo, size_t size)
{
    (void)size;
    return spiffsDoRotulo(rotulo) ? ESP_OK : ESP_ERR_INVALID_STATE;
}

/* =====================================================================
 * o FAT: na flash (FFat) e no cartao (SD) */
static esp_err_t montarFat(const char *base, fs::FS *fs, Tipo tipo)
{
    if (!base) return ESP_ERR_INVALID_ARG;
    if (acharMontagem(base)) return ESP_ERR_INVALID_STATE;
    montagens().push_back({base, "", fs, tipo});
    return ESP_OK;
}
esp_err_t esp_vfs_fat_spiflash_mount_rw_wl(const char *base, const char *rotulo, const esp_vfs_fat_mount_config_t *cfg, wl_handle_t *wl)
{
    (void)rotulo;
    if (!cfg) return ESP_ERR_INVALID_ARG;
    if (!FFat.begin(cfg->format_if_mount_failed, base)) return ESP_FAIL;
    if (wl) *wl = 0;
    return montarFat(base, &FFat, T_FAT);
}
esp_err_t esp_vfs_fat_spiflash_unmount_rw_wl(const char *base, wl_handle_t wl)
{
    (void)wl;
    auto &v = montagens();
    for (size_t i = 0; i < v.size(); i++)
        if (v[i].base == base) {
            FFat.end();
            v.erase(v.begin() + (long)i);
            return ESP_OK;
        }
    return ESP_ERR_INVALID_STATE;
}
static sdmmc_card_t *cartaoNovo()
{
    auto *c = (sdmmc_card_t *)calloc(1, sizeof(sdmmc_card_t));
    c->csd.capacity = (int)(SD.cardSize() / 512);
    c->csd.sector_size = 512;
    c->real_freq_khz = 20000;
    c->max_freq_khz = 20000;
    snprintf(c->cid.name, sizeof(c->cid.name), "SIM");
    return c;
}
esp_err_t esp_vfs_fat_sdmmc_mount(const char *base, const sdmmc_host_t *host, const void *slot, const esp_vfs_fat_mount_config_t *cfg, sdmmc_card_t **saida)
{
    (void)host;
    (void)slot;
    (void)cfg;
    if (!SD.begin()) return ESP_FAIL;
    esp_err_t e = montarFat(base, &SD, T_SD);
    if (e == ESP_OK && saida) *saida = cartaoNovo();
    return e;
}
esp_err_t esp_vfs_fat_sdspi_mount(const char *base, const sdmmc_host_t *host, const sdspi_device_config_t *slot, const esp_vfs_fat_mount_config_t *cfg, sdmmc_card_t **saida)
{
    (void)host;
    (void)cfg;
    if (!slot) return ESP_ERR_INVALID_ARG;
    if (!SD.begin((uint8_t)slot->gpio_cs)) return ESP_FAIL;
    esp_err_t e = montarFat(base, &SD, T_SD);
    if (e == ESP_OK && saida) *saida = cartaoNovo();
    return e;
}
esp_err_t esp_vfs_fat_sdcard_unmount(const char *base, sdmmc_card_t *cartao)
{
    auto &v = montagens();
    for (size_t i = 0; i < v.size(); i++)
        if (v[i].base == base) {
            SD.end();
            v.erase(v.begin() + (long)i);
            free(cartao);
            return ESP_OK;
        }
    return ESP_ERR_INVALID_ARG;
}
esp_err_t esp_vfs_fat_info(const char *base, uint64_t *total, uint64_t *livre)
{
    Montagem *m = acharMontagem(base);
    if (!m || m->tipo == T_SPIFFS) return ESP_ERR_INVALID_STATE;
    uint64_t t = m->tipo == T_SD ? SD.totalBytes() : FFat.totalBytes();
    uint64_t u = m->tipo == T_SD ? SD.usedBytes() : FFat.usedBytes();
    if (total) *total = t;
    if (livre) *livre = t - u;
    return ESP_OK;
}
void sdmmc_card_print_info(FILE *stream, const sdmmc_card_t *c)
{
    fprintf(stream ? stream : stdout, "Name: %s\nType: SDHC\nSpeed: 20.00 MHz\nSize: %lluMB\n", c->cid.name, ((unsigned long long)c->csd.capacity) * c->csd.sector_size / (1024 * 1024));
}
} /* extern "C" */
#endif
