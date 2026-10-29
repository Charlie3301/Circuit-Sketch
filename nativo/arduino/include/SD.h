/* SD.h -- o cartao microSD do circuito (a SD do Arduino e a da ESP32): os
 * arquivos sao os do cartao simulado, que o simulador mostra e guarda. */
#ifndef __SD_H__
#define __SD_H__

#include "cs_objeto.h"
#include "SPI.h"

/* Na ESP32 os modos sao os do C ("w" comeca do zero); no Uno e no ESP8266,
 * numeros (o FILE_WRITE escreve no fim). */
#ifdef ESP32
#define FILE_READ "r"
#define FILE_WRITE "w"
#define FILE_APPEND "a"
#else
#define FILE_READ ((uint8_t)0)
#define FILE_WRITE ((uint8_t)1)
#define FILE_APPEND ((uint8_t)2)
#endif
#ifndef O_READ
#define O_READ 0
#endif

class File : public Stream, public CsObjeto {
public:
    File() {}
    static File adotar(const String &nome)
    {
        File f;
        if (nome.length()) f.cs_adotar(nome);
        return f;
    }

    /* O texto (UTF-8) vai como texto -- o cartao mostra o acento --; o resto,
     * byte a byte (o arquivo binario, o byte acima de 127 sozinho). */
    size_t write(uint8_t c) override { return write(&c, 1); }
    size_t write(const uint8_t *b, size_t n) override
    {
        if (!cs_nome[0] || !n) return 0;
        if (cs_texto_utf8(b, n)) cs_obj_n(cs_nome, "print", cs_tn((const char *)b, (int)n));
        else cs_obj_n(cs_nome, "__escrever_bytes", cs_vetor(b, (int)n));
        return n;
    }
    using Print::write;
    int available() override { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "available") : 0; }
    int read() override { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "read") : -1; }
    /* Em blocos de 32 KB: o arquivo de varios MB numa chamada por pedaco. */
    int read(void *buf, size_t n)
    {
        if (!cs_nome[0]) return 0;
        uint8_t *p = (uint8_t *)buf;
        size_t k = 0;
        while (k < n) {
            size_t pedaco = n - k > 32768 ? 32768 : n - k;
            int lidos = (int)cs_obj_i(cs_nome, "__ler_bytes", cs_vetor_mutavel(p + k, (int)pedaco));
            if (lidos <= 0) break;
            k += (size_t)lidos;
            if ((size_t)lidos < pedaco) break;
        }
        return (int)k;
    }
    size_t readBytes(char *buf, size_t n) override { return (size_t)read(buf, n); }
    using Stream::readBytes;
    int peek() override { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "peek") : -1; }
    void flush() override { if (cs_nome[0]) cs_obj_n(cs_nome, "flush"); }
    String readString() override { return cs_nome[0] ? cs_obj_t(cs_nome, "readString") : String(); }
    String readStringUntil(char terminator) override
    {
        return cs_nome[0] ? cs_obj_t(cs_nome, "readStringUntil", terminator) : String();
    }
    void close() { if (cs_nome[0]) cs_obj_n(cs_nome, "close"); }
    size_t size() { return cs_nome[0] ? (size_t)cs_obj_i(cs_nome, "size") : 0; }
    size_t position() { return cs_nome[0] ? (size_t)cs_obj_i(cs_nome, "position") : 0; }
    bool seek(uint32_t pos) { return cs_nome[0] && cs_obj_i(cs_nome, "seek", pos) != 0; }
    const char *name()
    {
        static String n;
        n = cs_nome[0] ? cs_obj_t(cs_nome, "name") : String();
        return n.c_str();
    }
    bool isDirectory() { return cs_nome[0] && cs_obj_i(cs_nome, "isDirectory") != 0; }
    File openNextFile(const char *mode = "r") { (void)mode; return adotar(cs_nome[0] ? cs_obj_t(cs_nome, "openNextFile") : String()); }
    void rewindDirectory() { if (cs_nome[0]) cs_obj_n(cs_nome, "rewindDirectory"); }
    operator bool() { return cs_nome[0] != 0; }
};

/* O sistema de arquivos do core da ESP32: a funcao que recebe "fs::FS &fs"
 * aceita o SD e o SD_MMC (o cartao e um so, o do circuito). */
namespace fs {
typedef ::File File;
enum SeekMode { SeekSet = 0, SeekCur = 1, SeekEnd = 2 };
class FS {
protected:
    /* Quem guarda os arquivos: o cartao ("SD") ou a flash ("LittleFS",
     * "SPIFFS", "FFat"). */
    const char *cs_alvo;

public:
    explicit FS(const char *alvo = "SD") : cs_alvo(alvo) {}
    virtual ~FS() {}
    File open(const char *path, const char *mode = "r", bool create = false)
    {
        (void)create;
        if (cs_obj_i(cs_alvo, "open", path, mode) == 0) return File();
        cs_val v[] = {cs_t("File")};
        return File::adotar(cs_texto(nullptr, "__criar", 1, v));
    }
    File open(const String &path, const char *mode = "r", bool create = false) { return open(path.c_str(), mode, create); }
    bool exists(const char *path) { return cs_obj_i(cs_alvo, "exists", path) != 0; }
    bool exists(const String &path) { return exists(path.c_str()); }
    bool remove(const char *path) { return cs_obj_i(cs_alvo, "remove", path) != 0; }
    bool remove(const String &path) { return remove(path.c_str()); }
    bool rename(const char *de, const char *para) { return cs_obj_i(cs_alvo, "rename", de, para) != 0; }
    bool rename(const String &de, const String &para) { return rename(de.c_str(), para.c_str()); }
    bool mkdir(const char *path) { return cs_obj_i(cs_alvo, "mkdir", path) != 0; }
    bool mkdir(const String &path) { return mkdir(path.c_str()); }
    bool rmdir(const char *path) { return cs_obj_i(cs_alvo, "rmdir", path) != 0; }
    bool rmdir(const String &path) { return rmdir(path.c_str()); }
};
} // namespace fs
using fs::FS;

#include "sd_defines.h"

class SDClass : public fs::FS {
public:
    using fs::FS::open;
    using fs::FS::exists;
    using fs::FS::remove;
    using fs::FS::mkdir;
    using fs::FS::rmdir;
    bool begin(uint8_t csPin = 4) { return montado = cs_obj_i("SD", "begin", csPin) != 0; }
    /* O barramento que o sketch deu (o SPIClass(FSPI) nos pinos dele). */
    bool begin(uint8_t csPin, SPIClass &spi, uint32_t frequency = 4000000, const char *mountpoint = "/sd",
               uint8_t max_files = 5, bool format_if_empty = false)
    {
        (void)frequency;
        (void)mountpoint;
        (void)max_files;
        (void)format_if_empty;
        return montado = cs_obj_i("SD", "begin", csPin, cs_de(spi)) != 0;
    }
    void end()
    {
        montado = false;
        cs_obj_n("SD", "end");
    }
    /* O modo em texto, como o do C: o FILE_WRITE da ESP32 e o "w" (comeca do
     * zero); o do Uno e do ESP8266 escreve no fim. O arquivo aberto fica
     * esperando o File que o simulador cria em seguida. */
#ifndef ESP32
    File open(const char *path, uint8_t mode = FILE_READ)
    {
#ifdef ESP32
        const char *escrita = "w";
#else
        const char *escrita = "a";
#endif
        return open(path, mode == FILE_READ ? "r" : mode == FILE_APPEND ? "a" : escrita);
    }
    File open(const String &path, uint8_t mode = FILE_READ) { return open(path.c_str(), mode); }
#endif
    /* O cartao do simulador e um microSD de 4 GB (SDHC); sem begin, nenhum. */
    uint64_t cardSize() { return montado ? 4ULL * 1024 * 1024 * 1024 : 0; }
    sdcard_type_t cardType() { return montado ? CARD_SDHC : CARD_NONE; }
    uint64_t totalBytes() { return montado ? cardSize() - 4ULL * 1024 * 1024 : 0; }
    uint64_t usedBytes() { return 0; }
    uint8_t numSectors() { return 0; }
    size_t sectorSize() { return 512; }

private:
    bool montado = false;
};

inline SDClass SD;


#endif
