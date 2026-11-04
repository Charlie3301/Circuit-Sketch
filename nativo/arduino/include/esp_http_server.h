/* esp_http_server.h -- o do IDF; o HTTPD_DEFAULT_CONFIG usa os MALLOC_CAP_*, que
 * na placa ja vem de outro cabecalho do FreeRTOS. */
#ifndef CS_ESP_HTTP_SERVER_H
#define CS_ESP_HTTP_SERVER_H
#include "esp_heap_caps.h"
#include_next <esp_http_server.h>
#endif
