/* nvs.h: as preferencias gravadas na flash. Vivem enquanto a placa estiver
 * no circuito aberto: reiniciar a placa nao apaga, como na de verdade. */
#ifndef CS_NVS_H
#define CS_NVS_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include "esp_err.h"

typedef uint32_t nvs_handle_t;
typedef nvs_handle_t nvs_handle;
typedef enum { NVS_READONLY, NVS_READWRITE } nvs_open_mode_t;
typedef nvs_open_mode_t nvs_open_mode;
typedef enum {
    NVS_TYPE_U8 = 0x01, NVS_TYPE_I8 = 0x11, NVS_TYPE_U16 = 0x02, NVS_TYPE_I16 = 0x12,
    NVS_TYPE_U32 = 0x04, NVS_TYPE_I32 = 0x14, NVS_TYPE_U64 = 0x08, NVS_TYPE_I64 = 0x18,
    NVS_TYPE_STR = 0x21, NVS_TYPE_BLOB = 0x42, NVS_TYPE_ANY = 0xff
} nvs_type_t;

#define NVS_DEFAULT_PART_NAME "nvs"
#define NVS_PART_NAME_MAX_SIZE 16
#define NVS_KEY_NAME_MAX_SIZE 16
#define NVS_NS_NAME_MAX_SIZE NVS_KEY_NAME_MAX_SIZE

esp_err_t nvs_open(const char *espaco, nvs_open_mode_t modo, nvs_handle_t *alca);
esp_err_t nvs_open_from_partition(const char *particao, const char *espaco, nvs_open_mode_t modo,
                                  nvs_handle_t *alca);
void nvs_close(nvs_handle_t alca);
esp_err_t nvs_commit(nvs_handle_t alca);
esp_err_t nvs_erase_key(nvs_handle_t alca, const char *chave);
esp_err_t nvs_erase_all(nvs_handle_t alca);

esp_err_t nvs_set_i8(nvs_handle_t alca, const char *chave, int8_t valor);
esp_err_t nvs_set_u8(nvs_handle_t alca, const char *chave, uint8_t valor);
esp_err_t nvs_set_i16(nvs_handle_t alca, const char *chave, int16_t valor);
esp_err_t nvs_set_u16(nvs_handle_t alca, const char *chave, uint16_t valor);
esp_err_t nvs_set_i32(nvs_handle_t alca, const char *chave, int32_t valor);
esp_err_t nvs_set_u32(nvs_handle_t alca, const char *chave, uint32_t valor);
esp_err_t nvs_set_i64(nvs_handle_t alca, const char *chave, int64_t valor);
esp_err_t nvs_set_u64(nvs_handle_t alca, const char *chave, uint64_t valor);
esp_err_t nvs_set_str(nvs_handle_t alca, const char *chave, const char *valor);
esp_err_t nvs_set_blob(nvs_handle_t alca, const char *chave, const void *valor, size_t n);

esp_err_t nvs_get_i8(nvs_handle_t alca, const char *chave, int8_t *valor);
esp_err_t nvs_get_u8(nvs_handle_t alca, const char *chave, uint8_t *valor);
esp_err_t nvs_get_i16(nvs_handle_t alca, const char *chave, int16_t *valor);
esp_err_t nvs_get_u16(nvs_handle_t alca, const char *chave, uint16_t *valor);
esp_err_t nvs_get_i32(nvs_handle_t alca, const char *chave, int32_t *valor);
esp_err_t nvs_get_u32(nvs_handle_t alca, const char *chave, uint32_t *valor);
esp_err_t nvs_get_i64(nvs_handle_t alca, const char *chave, int64_t *valor);
esp_err_t nvs_get_u64(nvs_handle_t alca, const char *chave, uint64_t *valor);
esp_err_t nvs_get_str(nvs_handle_t alca, const char *chave, char *valor, size_t *n);
esp_err_t nvs_get_blob(nvs_handle_t alca, const char *chave, void *valor, size_t *n);

#endif
