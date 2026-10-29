/* SD.h -- o cartao microSD do circuito (a SD do Arduino e a da ESP32): os
 * arquivos sao os do cartao simulado, que o simulador mostra e guarda. */
#ifndef __SD_H__
#define __SD_H__

#include "cs_objeto.h"
#include "SPI.h"

#define FILE_READ 0
#define FILE_WRITE 1
#define FILE_APPEND 2
#ifndef O_READ
#define O_READ FILE_READ
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

    size_t write(uint8_t c) override
    {
        char t[2] = {(char)c, 0};
        if (cs_nome[0]) cs_obj_n(cs_nome, "write", cs_tn(t, 1));
        return 1;
    }
    size_t write(const uint8_t *b, size_t n) override
    {
        if (cs_nome[0]) cs_obj_n(cs_nome, "print", cs_tn((const char *)b, (int)n));
        return n;
    }
    using Print::write;
    int available() override { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "available") : 0; }
    int read() override { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "read") : -1; }
    int read(void *buf, size_t n)
    {
        uint8_t *p = (uint8_t *)buf;
        size_t k = 0;
        while (k < n) {
            int c = read();
            if (c < 0) break;
            p[k++] = (uint8_t)c;
        }
        return (int)k;
    }
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

class SDClass {
public:
    bool begin(uint8_t csPin = 4) { return cs_obj_i("SD", "begin", csPin) != 0; }
    bool begin(uint8_t csPin, SPIClass &spi, uint32_t frequency = 4000000, const char *mountpoint = "/sd",
               uint8_t max_files = 5, bool format_if_empty = false)
    {
        (void)spi;
        (void)frequency;
        (void)mountpoint;
        (void)max_files;
        (void)format_if_empty;
        return begin(csPin);
    }
    void end() { cs_obj_n("SD", "end"); }
    /* O modo em texto, como o do C: o FILE_WRITE da ESP32 e o "w" (comeca do
     * zero); o do Uno e do ESP8266 escreve no fim. O arquivo aberto fica
     * esperando o File que o simulador cria em seguida. */
    File open(const char *path, const char *mode, bool create = false)
    {
        (void)create;
        if (cs_obj_i("SD", "open", path, mode) == 0) return File();
        cs_val v[] = {cs_t("File")};
        return File::adotar(cs_texto(nullptr, "__criar", 1, v));
    }
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
    bool exists(const char *path) { return cs_obj_i("SD", "exists", path) != 0; }
    bool exists(const String &path) { return exists(path.c_str()); }
    bool remove(const char *path) { return cs_obj_i("SD", "remove", path) != 0; }
    bool mkdir(const char *path) { return cs_obj_i("SD", "mkdir", path) != 0; }
    bool rmdir(const char *path) { return cs_obj_i("SD", "rmdir", path) != 0; }
    uint64_t cardSize() { return 4ULL * 1024 * 1024 * 1024; }
    uint8_t cardType() { return 2; }
};

inline SDClass SD;

#define CARD_NONE 0
#define CARD_MMC 1
#define CARD_SD 2
#define CARD_SDHC 3

#endif
