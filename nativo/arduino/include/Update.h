/* Update.h -- a atualizacao do firmware pela rede (o OTA) da ESP32: o Update
 * recebe o binario novo em pedacos, confere e marca para a placa iniciar
 * por ele.
 *
 * No simulador a conferencia e a da placa: o tamanho, o byte magico do
 * firmware (0xE9), o MD5/SHA pedidos. O que muda e o fim: o binario e
 * codigo da ESP32, e a placa simulada continua com o sketch do editor
 * depois do ESP.restart() -- o simulador avisa (cs_update.cpp). A mesma API
 * do core oficial. */
#ifndef ESP32UPDATER_H
#define ESP32UPDATER_H

#include <Arduino.h>
#include <MD5Builder.h>
#include <functional>
#include "esp_partition.h"

#define UPDATE_ERROR_OK (0)
#define UPDATE_ERROR_WRITE (1)
#define UPDATE_ERROR_ERASE (2)
#define UPDATE_ERROR_READ (3)
#define UPDATE_ERROR_SPACE (4)
#define UPDATE_ERROR_SIZE (5)
#define UPDATE_ERROR_STREAM (6)
#define UPDATE_ERROR_MD5 (7)
#define UPDATE_ERROR_MAGIC_BYTE (8)
#define UPDATE_ERROR_ACTIVATE (9)
#define UPDATE_ERROR_NO_PARTITION (10)
#define UPDATE_ERROR_BAD_ARGUMENT (11)
#define UPDATE_ERROR_ABORT (12)
#define UPDATE_ERROR_DECRYPT (13)
#define UPDATE_ERROR_SIGN (14)
#define UPDATE_ERROR_SHA256 (15)
#define UPDATE_ERROR_SHA512 (16)

#define UPDATE_SIZE_UNKNOWN 0xFFFFFFFF

#define U_FLASH 0
#define U_FLASHFS 100
#define U_SPIFFS 101
#define U_FATFS 102
#define U_LITTLEFS 103
#define U_AUTH 200

#define ENCRYPTED_BLOCK_SIZE 16
#define ENCRYPTED_TWEAK_BLOCK_SIZE 32
#define ENCRYPTED_KEY_SIZE 32
#define U_AES_DECRYPT_NONE 0
#define U_AES_DECRYPT_AUTO 1
#define U_AES_DECRYPT_ON 2
#define U_AES_DECRYPT_MODE_MASK 3
#define U_AES_IMAGE_DECRYPTING_BIT 4

#define SPI_SECTORS_PER_BLOCK 16
#ifndef SPI_FLASH_SEC_SIZE
#define SPI_FLASH_SEC_SIZE 4096
#endif
#define SPI_FLASH_BLOCK_SIZE (SPI_SECTORS_PER_BLOCK * SPI_FLASH_SEC_SIZE)

class UpdateClass {
public:
    typedef std::function<void(size_t, size_t)> THandlerFunction_Progress;

    UpdateClass() {}
    /* A assinatura do firmware: a imagem daqui nao passa pelo verificador. */
    bool installSignature(class UpdaterVerifyClass *) { return true; }
    UpdateClass &onProgress(THandlerFunction_Progress fn)
    {
        _progress_callback = fn;
        return *this;
    }
    bool begin(size_t size = UPDATE_SIZE_UNKNOWN, int command = U_FLASH, int ledPin = -1, uint8_t ledOn = LOW, const char *label = NULL);
    bool setupCrypt(const uint8_t *cryptKey = 0, size_t cryptAddress = 0, uint8_t cryptConfig = 0xf, int cryptMode = U_AES_DECRYPT_AUTO)
    {
        (void)cryptKey, (void)cryptAddress, (void)cryptConfig, (void)cryptMode;
        return true;
    }
    size_t write(uint8_t *data, size_t len);
    size_t writeStream(Stream &data);
    void setStreamTimeout(uint32_t timeoutMs) { _streamTimeout = timeoutMs; }
    bool end(bool evenIfRemaining = false);
    bool setCryptKey(const uint8_t *cryptKey)
    {
        (void)cryptKey;
        return true;
    }
    bool setCryptMode(const int cryptMode)
    {
        (void)cryptMode;
        return true;
    }
    void setCryptAddress(const size_t cryptAddress) { (void)cryptAddress; }
    void setCryptConfig(const uint8_t cryptConfig) { (void)cryptConfig; }
    void abort();
    void printError(Print &out) { out.printf("Error[%u]: %s\n", _error, errorString()); }
    const char *errorString();
    bool setMD5(const char *expected_md5, bool calc_post_decryption = true);
    String md5String(void) { return _md5.toString(); }
    void md5(uint8_t *result) { _md5.getBytes(result); }
    bool setSHA256(const char *expected_sha256, bool calc_post_decryption = true);
    String sha256String(void);
    void sha256(uint8_t *result);
    bool setSHA512(const char *expected_sha512, bool calc_post_decryption = true);
    String sha512String(void);
    void sha512(uint8_t *result);

    uint8_t getError() { return _error; }
    void clearError() { _error = UPDATE_ERROR_OK; }
    bool hasError() { return _error != UPDATE_ERROR_OK; }
    bool isRunning() { return _size > 0; }
    bool isFinished() { return _progress == _size; }
    size_t size() { return _size; }
    size_t progress() { return _progress; }
    size_t remaining() { return _size - _progress; }

    template <typename T> size_t write(T &data)
    {
        size_t written = 0;
        uint8_t buf[1024];
        while (!hasError() && isRunning() && remaining()) {
            int disponivel = data.available();
            if (disponivel <= 0) break;
            size_t n = (size_t)disponivel < sizeof(buf) ? (size_t)disponivel : sizeof(buf);
            if (n > remaining()) n = remaining();
            n = data.readBytes((char *)buf, n);
            if (!n || write(buf, n) != n) break;
            written += n;
        }
        return written;
    }

    bool canRollBack() { return _foi; }
    bool rollBack()
    {
        bool r = _foi;
        _foi = false;
        return r;
    }

private:
    void _reset();
    void _abort(uint8_t err);
    void _hash(const uint8_t *d, size_t n);

    uint8_t _error = UPDATE_ERROR_OK;
    size_t _size = 0;
    uint32_t _progress = 0;
    uint32_t _command = U_FLASH;
    uint32_t _streamTimeout = 60000;
    int _ledPin = -1;
    uint8_t _ledOn = LOW;
    bool _foi = false;
    THandlerFunction_Progress _progress_callback;
    String _target_md5, _target_sha256, _target_sha512;
    MD5Builder _md5;
    void *_sha256 = nullptr;
    void *_sha512 = nullptr;
    uint8_t _primeiro = 0;
};

#if !defined(NO_GLOBAL_INSTANCES) && !defined(NO_GLOBAL_UPDATE)
extern UpdateClass Update;
#endif

#endif
