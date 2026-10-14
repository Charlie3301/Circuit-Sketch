/* esp_log.h: ESP_LOGx como no ESP-IDF 5, "I (1234) TAG: texto" pelo
 * monitor serial.
 *
 * O nivel e o do sdkconfig da placa. No core do Arduino ele e ERROR: um
 * ESP_LOGI num .c nem entra no programa (LOG_LOCAL_LEVEL), e em tempo de
 * execucao so passa o que esp_log_level_set() liberar -- exatamente o que
 * acontece na placa. */
#ifndef CS_ESP_LOG_H
#define CS_ESP_LOG_H

#include <stdint.h>
#include <stdarg.h>
#include "sdkconfig.h"

typedef enum {
    ESP_LOG_NONE, ESP_LOG_ERROR, ESP_LOG_WARN, ESP_LOG_INFO, ESP_LOG_DEBUG, ESP_LOG_VERBOSE, ESP_LOG_MAX
} esp_log_level_t;

#ifndef LOG_LOCAL_LEVEL
#define LOG_LOCAL_LEVEL CONFIG_LOG_MAXIMUM_LEVEL
#endif

void esp_log_write(esp_log_level_t nivel, const char *tag, const char *formato, ...);
void esp_log_writev(esp_log_level_t nivel, const char *tag, const char *formato, va_list ap);
void esp_log_level_set(const char *tag, esp_log_level_t nivel);
esp_log_level_t esp_log_level_get(const char *tag);
uint32_t esp_log_timestamp(void);
uint32_t esp_log_early_timestamp(void);
char *esp_log_system_timestamp(void);
void esp_log_buffer_hex_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel);
void esp_log_buffer_char_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel);
void esp_log_buffer_hexdump_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel);
typedef int (*vprintf_like_t)(const char *, va_list);
vprintf_like_t esp_log_set_vprintf(vprintf_like_t funcao);

#define ESP_LOG_LEVEL(nivel, tag, formato, ...) esp_log_write(nivel, tag, formato, ##__VA_ARGS__)
#define ESP_LOG_LEVEL_LOCAL(nivel, tag, formato, ...) do { if (LOG_LOCAL_LEVEL >= nivel) esp_log_write(nivel, tag, formato, ##__VA_ARGS__); } while (0)
#define ESP_LOGE(tag, formato, ...) ESP_LOG_LEVEL_LOCAL(ESP_LOG_ERROR, tag, formato, ##__VA_ARGS__)
#define ESP_LOGW(tag, formato, ...) ESP_LOG_LEVEL_LOCAL(ESP_LOG_WARN, tag, formato, ##__VA_ARGS__)
#define ESP_LOGI(tag, formato, ...) ESP_LOG_LEVEL_LOCAL(ESP_LOG_INFO, tag, formato, ##__VA_ARGS__)
#define ESP_LOGD(tag, formato, ...) ESP_LOG_LEVEL_LOCAL(ESP_LOG_DEBUG, tag, formato, ##__VA_ARGS__)
#define ESP_LOGV(tag, formato, ...) ESP_LOG_LEVEL_LOCAL(ESP_LOG_VERBOSE, tag, formato, ##__VA_ARGS__)
#define ESP_EARLY_LOGE ESP_LOGE
#define ESP_EARLY_LOGW ESP_LOGW
#define ESP_EARLY_LOGI ESP_LOGI
#define ESP_EARLY_LOGD ESP_LOGD
#define ESP_EARLY_LOGV ESP_LOGV
#define ESP_DRAM_LOGE ESP_LOGE
#define ESP_DRAM_LOGW ESP_LOGW
#define ESP_DRAM_LOGI ESP_LOGI
#define ESP_DRAM_LOGD ESP_LOGD
#define ESP_DRAM_LOGV ESP_LOGV

#define ESP_LOG_BUFFER_HEX_LEVEL(tag, buf, n, nivel) do { if (LOG_LOCAL_LEVEL >= (nivel)) esp_log_buffer_hex_internal(tag, buf, n, nivel); } while (0)
#define ESP_LOG_BUFFER_CHAR_LEVEL(tag, buf, n, nivel) do { if (LOG_LOCAL_LEVEL >= (nivel)) esp_log_buffer_char_internal(tag, buf, n, nivel); } while (0)
#define ESP_LOG_BUFFER_HEXDUMP(tag, buf, n, nivel) do { if (LOG_LOCAL_LEVEL >= (nivel)) esp_log_buffer_hexdump_internal(tag, buf, n, nivel); } while (0)
#define ESP_LOG_BUFFER_HEX(tag, buf, n) ESP_LOG_BUFFER_HEX_LEVEL(tag, buf, n, ESP_LOG_INFO)
#define ESP_LOG_BUFFER_CHAR(tag, buf, n) ESP_LOG_BUFFER_CHAR_LEVEL(tag, buf, n, ESP_LOG_INFO)
#define esp_log_buffer_hex(tag, buf, n) ESP_LOG_BUFFER_HEX(tag, buf, n)
#define esp_log_buffer_char(tag, buf, n) ESP_LOG_BUFFER_CHAR(tag, buf, n)

#endif
