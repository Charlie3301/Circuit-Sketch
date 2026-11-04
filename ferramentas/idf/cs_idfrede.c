/* cs_idfrede.c -- o que a rede de alto nivel do IDF precisa e a placa tem em outro
 * componente: o pacote de certificados. O da placa e um binario gravado na flash;
 * aqui nao ha lista, entao a conexao por ele aceita o servidor sem conferir. */
#include "esp_crt_bundle.h"
#include "mbedtls/ssl.h"
#include "esp_err.h"

esp_err_t esp_crt_bundle_attach(void *conf)
{
    mbedtls_ssl_conf_authmode((mbedtls_ssl_config *)conf, MBEDTLS_SSL_VERIFY_NONE);
    return ESP_OK;
}
void esp_crt_bundle_detach(mbedtls_ssl_config *conf) { (void)conf; }
esp_err_t esp_crt_bundle_set(const uint8_t *x509_bundle, size_t bundle_size)
{
    (void)x509_bundle;
    (void)bundle_size;
    return ESP_OK;
}
