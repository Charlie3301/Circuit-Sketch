/* cs_mbedtls_config.h -- a configuracao do mbedTLS nas placas ESP32 (o
 * MBEDTLS_CONFIG_FILE que o simulador passa, como o IDF passa o dele).
 *
 * E a do ESP-IDF (mbedtls/esp_config.h, com as opcoes do sdkconfig do chip),
 * menos o que na placa e o hardware do chip: o AES, o GCM, o SHA, o MD5, o
 * RSA e as curvas elipticas ficam com a versao em software do proprio
 * mbedTLS (o mesmo resultado, so mais devagar na placa). O sorteio
 * (MBEDTLS_ENTROPY_HARDWARE_ALT) continua: o mbedtls_hardware_poll e o
 * esp_fill_random, como no IDF (src/cs_mbedtls.c). O relogio e o do
 * mbedTLS no Windows, e as travas entre tarefas ficam de fora (o FreeRTOS do
 * simulador roda uma tarefa de cada vez). */
#ifndef CS_MBEDTLS_CONFIG_H
#define CS_MBEDTLS_CONFIG_H

#include "mbedtls/esp_config.h"

#undef MBEDTLS_AES_ALT
#undef MBEDTLS_GCM_ALT
#undef MBEDTLS_GCM_NON_AES_CIPHER_SOFT_FALLBACK
#undef MBEDTLS_SHA1_ALT
#undef MBEDTLS_SHA256_ALT
#undef MBEDTLS_SHA512_ALT
#undef MBEDTLS_MD5_ALT
#undef MBEDTLS_MPI_EXP_MOD_ALT
#undef MBEDTLS_MPI_EXP_MOD_ALT_FALLBACK
#undef MBEDTLS_MPI_MUL_MPI_ALT
#undef MBEDTLS_ECDSA_SIGN_ALT
#undef MBEDTLS_ECDSA_VERIFY_ALT
#undef MBEDTLS_ECP_MUL_ALT
#undef MBEDTLS_ECP_MUL_ALT_SOFT_FALLBACK
#undef MBEDTLS_ECP_VERIFY_ALT
#undef MBEDTLS_ECP_VERIFY_ALT_SOFT_FALLBACK

#undef MBEDTLS_PLATFORM_MS_TIME_ALT
#undef MBEDTLS_PLATFORM_TIME_ALT

#undef MBEDTLS_THREADING_C
#undef MBEDTLS_THREADING_ALT
#undef MBEDTLS_THREADING_PTHREAD

#endif
