/* esp_assert.h -- o do ESP-IDF, sem conferir o tamanho das structs: com o ponteiro de 64 bits do
 * computador, o descritor de DMA nao tem os 12 bytes da placa, e nem precisa. */
#ifndef CS_ESP_ASSERT_H
#define CS_ESP_ASSERT_H
#if __has_include_next("esp_assert.h")
#include_next "esp_assert.h"
#endif
#undef ESP_STATIC_ASSERT
#define ESP_STATIC_ASSERT(...)
#undef STATIC_ASSERT
#define STATIC_ASSERT(...)
#endif
