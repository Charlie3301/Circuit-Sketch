/* cs_update.cpp -- o Update (OTA) da ESP32 no simulador (ver Update.h). */
#include "Arduino.h"
#if defined(ESP32)
#include "Update.h"
#include "SHA2Builder.h"

UpdateClass Update;

static SHA256Builder *sha256De(void *p) { return (SHA256Builder *)p; }
static SHA512Builder *sha512De(void *p) { return (SHA512Builder *)p; }

void UpdateClass::_reset()
{
    _size = 0;
    _progress = 0;
    _target_md5 = "";
    _target_sha256 = "";
    _target_sha512 = "";
    delete sha256De(_sha256);
    delete sha512De(_sha512);
    _sha256 = _sha512 = nullptr;
    if (_ledPin >= 0) digitalWrite(_ledPin, !_ledOn);
}

void UpdateClass::_abort(uint8_t err)
{
    _reset();
    _error = err;
}

void UpdateClass::abort() { _abort(UPDATE_ERROR_ABORT); }

bool UpdateClass::begin(size_t size, int command, int ledPin, uint8_t ledOn, const char *label)
{
    (void)label;
    if (_size > 0) {
        log_w("already running");
        return false;
    }
    _ledPin = ledPin;
    _ledOn = ledOn;
    _error = UPDATE_ERROR_OK;
    _target_md5 = "";
    if (size == 0) {
        _error = UPDATE_ERROR_SIZE;
        return false;
    }
    /* A particao de app do esquema padrao (0x140000) e a de dados (0x160000). */
    size_t cabe = command == U_FLASH ? 0x140000 : 0x160000;
    if (size == UPDATE_SIZE_UNKNOWN) size = cabe;
    if (size > cabe) {
        log_e("too large %u > %u", (unsigned)size, (unsigned)cabe);
        _error = UPDATE_ERROR_SIZE;
        return false;
    }
    if (ledPin >= 0) pinMode(ledPin, OUTPUT);
    _command = command;
    _size = size;
    _progress = 0;
    _md5.begin();
    return true;
}

bool UpdateClass::setMD5(const char *expected_md5, bool calc_post_decryption)
{
    (void)calc_post_decryption;
    if (strlen(expected_md5) != 32) return false;
    _target_md5 = expected_md5;
    _target_md5.toLowerCase();
    return true;
}

bool UpdateClass::setSHA256(const char *expected_sha256, bool calc_post_decryption)
{
    (void)calc_post_decryption;
    if (strlen(expected_sha256) != 64) return false;
    _target_sha256 = expected_sha256;
    _target_sha256.toLowerCase();
    if (!_sha256) {
        _sha256 = new SHA256Builder();
        sha256De(_sha256)->begin();
    }
    return true;
}

bool UpdateClass::setSHA512(const char *expected_sha512, bool calc_post_decryption)
{
    (void)calc_post_decryption;
    if (strlen(expected_sha512) != 128) return false;
    _target_sha512 = expected_sha512;
    _target_sha512.toLowerCase();
    if (!_sha512) {
        _sha512 = new SHA512Builder();
        sha512De(_sha512)->begin();
    }
    return true;
}

String UpdateClass::sha256String(void) { return _sha256 ? sha256De(_sha256)->toString() : String(); }
void UpdateClass::sha256(uint8_t *result)
{
    if (_sha256) sha256De(_sha256)->getBytes(result);
}
String UpdateClass::sha512String(void) { return _sha512 ? sha512De(_sha512)->toString() : String(); }
void UpdateClass::sha512(uint8_t *result)
{
    if (_sha512) sha512De(_sha512)->getBytes(result);
}

void UpdateClass::_hash(const uint8_t *d, size_t n)
{
    _md5.add(d, n);
    if (_sha256) sha256De(_sha256)->add(d, n);
    if (_sha512) sha512De(_sha512)->add(d, n);
}

size_t UpdateClass::write(uint8_t *data, size_t len)
{
    if (hasError() || !isRunning()) return 0;
    if (len > remaining()) {
        _abort(UPDATE_ERROR_SPACE);
        return 0;
    }
    /* O firmware da ESP32 comeca com o byte magico 0xE9 (o cabecalho da imagem). */
    if (_progress == 0 && len > 0 && _command == U_FLASH) {
        _primeiro = data[0];
        if (data[0] != 0xE9) {
            log_e("Magic byte is wrong, not 0xE9");
            _abort(UPDATE_ERROR_MAGIC_BYTE);
            return 0;
        }
    }
    if (_ledPin >= 0) digitalWrite(_ledPin, (_progress / 4096) % 2 ? _ledOn : !_ledOn);
    _hash(data, len);
    _progress += len;
    if (_progress_callback) _progress_callback(_progress, _size);
    return len;
}

size_t UpdateClass::writeStream(Stream &data)
{
    size_t escritos = 0;
    uint8_t buf[1024];
    if (hasError() || !isRunning()) return 0;
    if (_progress_callback) _progress_callback(0, _size);
    while (remaining()) {
        size_t quer = remaining() < sizeof(buf) ? remaining() : sizeof(buf);
        size_t n = data.readBytes((char *)buf, quer);
        if (n == 0) {
            uint32_t ate = millis() + _streamTimeout;
            while (!data.available() && millis() < ate) delay(10);
            if (!data.available()) {
                _abort(UPDATE_ERROR_STREAM);
                return escritos;
            }
            continue;
        }
        if (write(buf, n) != n) return escritos;
        escritos += n;
    }
    return escritos;
}

bool UpdateClass::end(bool evenIfRemaining)
{
    if (hasError() || _size == 0) return false;
    if (!isFinished() && !evenIfRemaining) {
        log_e("premature end: res:%u, pos:%u/%u\n", getError(), (unsigned)progress(), (unsigned)_size);
        _abort(UPDATE_ERROR_ABORT);
        return false;
    }
    if (evenIfRemaining) _size = _progress;
    _md5.calculate();
    if (_target_md5.length() && _target_md5 != _md5.toString()) {
        _abort(UPDATE_ERROR_MD5);
        return false;
    }
    if (_sha256) {
        sha256De(_sha256)->calculate();
        if (_target_sha256.length() && _target_sha256 != sha256De(_sha256)->toString()) {
            _abort(UPDATE_ERROR_SHA256);
            return false;
        }
    }
    if (_sha512) {
        sha512De(_sha512)->calculate();
        if (_target_sha512.length() && _target_sha512 != sha512De(_sha512)->toString()) {
            _abort(UPDATE_ERROR_SHA512);
            return false;
        }
    }
    if (_command == U_FLASH) {
        _foi = true;
        log_printf("[Update] firmware novo de %u bytes conferido. A placa simulada continua com o sketch do editor depois de "
                   "reiniciar: o binario e codigo da ESP32, que o simulador nao executa.\n",
                   (unsigned)_progress);
    }
    _reset();
    return true;
}

const char *UpdateClass::errorString()
{
    switch (_error) {
    case UPDATE_ERROR_OK: return "No Error";
    case UPDATE_ERROR_WRITE: return "Flash Write Failed";
    case UPDATE_ERROR_ERASE: return "Flash Erase Failed";
    case UPDATE_ERROR_READ: return "Flash Read Failed";
    case UPDATE_ERROR_SPACE: return "Not Enough Space";
    case UPDATE_ERROR_SIZE: return "Bad Size Given";
    case UPDATE_ERROR_STREAM: return "Stream Read Timeout";
    case UPDATE_ERROR_MD5: return "MD5 Check Failed";
    case UPDATE_ERROR_MAGIC_BYTE: return "Wrong Magic Byte";
    case UPDATE_ERROR_ACTIVATE: return "Could Not Activate The Firmware";
    case UPDATE_ERROR_NO_PARTITION: return "Partition Could Not be Found";
    case UPDATE_ERROR_BAD_ARGUMENT: return "Bad Argument";
    case UPDATE_ERROR_ABORT: return "Aborted";
    case UPDATE_ERROR_DECRYPT: return "Decryption error";
    case UPDATE_ERROR_SIGN: return "Signature Verification Failed";
    case UPDATE_ERROR_SHA256: return "SHA256 Check Failed";
    case UPDATE_ERROR_SHA512: return "SHA512 Check Failed";
    default: return "UNKNOWN";
    }
}

#endif
