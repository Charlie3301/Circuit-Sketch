/* cs_mbedtls.c -- o que o port do mbedTLS no ESP-IDF da (components/mbedtls/port):
 * a memoria (esp_mem.c) e o sorteio do hardware (esp_hardware.c). */
#include "cs_placa.h"
#if defined(ESP32)
#include <stdlib.h>
#include <stddef.h>
#include "esp_random.h"

void *esp_mbedtls_mem_calloc(size_t n, size_t size) { return calloc(n, size); }
void esp_mbedtls_mem_free(void *ptr) { free(ptr); }

int mbedtls_hardware_poll(void *data, unsigned char *output, size_t len, size_t *olen)
{
    (void)data;
    esp_fill_random(output, len);
    *olen = len;
    return 0;
}
#endif
