/* esp_http_server.h: o servidor HTTP do ESP-IDF.
 *
 * Como na placa, os tratadores rodam UM de cada vez, na tarefa do
 * servidor: um tratador que nao volta (um fluxo SSE sem fim) segura o
 * servidor inteiro -- a nao ser que passe o pedido para outra tarefa com
 * httpd_req_async_handler_begin(). O navegador do computador chega pela
 * ponte em localhost que o painel Rede mostra. */
#ifndef CS_ESP_HTTP_SERVER_H
#define CS_ESP_HTTP_SERVER_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <string.h>
#include <sys/types.h>
#include "esp_err.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define HTTPD_MAX_REQ_HDR_LEN 512
#define HTTPD_MAX_URI_LEN 512
#define HTTPD_RESP_USE_STRLEN -1
#define HTTPD_SOCK_ERR_FAIL -1
#define HTTPD_SOCK_ERR_INVALID -2
#define HTTPD_SOCK_ERR_TIMEOUT -3

#define HTTPD_200 "200 OK"
#define HTTPD_204 "204 No Content"
#define HTTPD_207 "207 Multi-Status"
#define HTTPD_400 "400 Bad Request"
#define HTTPD_404 "404 Not Found"
#define HTTPD_408 "408 Request Timeout"
#define HTTPD_500 "500 Internal Server Error"

#define HTTPD_TYPE_JSON "application/json"
#define HTTPD_TYPE_TEXT "text/html"
#define HTTPD_TYPE_OCTET "application/octet-stream"

#define ESP_HTTPD_DEF_CTRL_PORT 32768

typedef enum http_method {
    HTTP_DELETE = 0, HTTP_GET = 1, HTTP_HEAD = 2, HTTP_POST = 3, HTTP_PUT = 4,
    HTTP_CONNECT = 5, HTTP_OPTIONS = 6, HTTP_TRACE = 7, HTTP_PATCH = 28,
} httpd_method_t;
#define HTTP_ANY -1

typedef void *httpd_handle_t;
typedef void (*httpd_free_ctx_fn_t)(void *ctx);
typedef esp_err_t (*httpd_open_func_t)(httpd_handle_t hd, int sockfd);
typedef void (*httpd_close_func_t)(httpd_handle_t hd, int sockfd);
typedef bool (*httpd_uri_match_func_t)(const char *reference_uri, const char *uri_to_match, size_t match_upto);

typedef struct httpd_config {
    unsigned task_priority;
    size_t stack_size;
    BaseType_t core_id;
    uint32_t task_caps;
    uint16_t server_port;
    uint16_t ctrl_port;
    uint16_t max_open_sockets;
    uint16_t max_uri_handlers;
    uint16_t max_resp_headers;
    uint16_t backlog_conn;
    bool lru_purge_enable;
    uint16_t recv_wait_timeout;
    uint16_t send_wait_timeout;
    void *global_user_ctx;
    httpd_free_ctx_fn_t global_user_ctx_free_fn;
    void *global_transport_ctx;
    httpd_free_ctx_fn_t global_transport_ctx_free_fn;
    bool enable_so_linger;
    int linger_timeout;
    bool keep_alive_enable;
    int keep_alive_idle;
    int keep_alive_interval;
    int keep_alive_count;
    httpd_open_func_t open_fn;
    httpd_close_func_t close_fn;
    httpd_uri_match_func_t uri_match_fn;
} httpd_config_t;

#define HTTPD_DEFAULT_CONFIG() { \
    .task_priority = tskIDLE_PRIORITY + 5, \
    .stack_size = 4096, \
    .core_id = tskNO_AFFINITY, \
    .task_caps = 0, \
    .server_port = 80, \
    .ctrl_port = ESP_HTTPD_DEF_CTRL_PORT, \
    .max_open_sockets = 7, \
    .max_uri_handlers = 8, \
    .max_resp_headers = 8, \
    .backlog_conn = 5, \
    .lru_purge_enable = false, \
    .recv_wait_timeout = 5, \
    .send_wait_timeout = 5, \
    .global_user_ctx = NULL, \
    .global_user_ctx_free_fn = NULL, \
    .global_transport_ctx = NULL, \
    .global_transport_ctx_free_fn = NULL, \
    .enable_so_linger = false, \
    .linger_timeout = 0, \
    .keep_alive_enable = false, \
    .keep_alive_idle = 0, \
    .keep_alive_interval = 0, \
    .keep_alive_count = 0, \
    .open_fn = NULL, \
    .close_fn = NULL, \
    .uri_match_fn = NULL \
}

typedef struct httpd_req {
    httpd_handle_t handle;
    int method;
    const char uri[HTTPD_MAX_URI_LEN + 1];
    size_t content_len;
    void *aux;
    void *user_ctx;
    void *sess_ctx;
    httpd_free_ctx_fn_t free_ctx;
    bool ignore_sess_ctx_changes;
} httpd_req_t;

typedef struct httpd_uri {
    const char *uri;
    httpd_method_t method;
    esp_err_t (*handler)(httpd_req_t *r);
    void *user_ctx;
} httpd_uri_t;

typedef enum {
    HTTPD_500_INTERNAL_SERVER_ERROR = 0,
    HTTPD_501_METHOD_NOT_IMPLEMENTED,
    HTTPD_505_VERSION_NOT_SUPPORTED,
    HTTPD_400_BAD_REQUEST,
    HTTPD_401_UNAUTHORIZED,
    HTTPD_403_FORBIDDEN,
    HTTPD_404_NOT_FOUND,
    HTTPD_405_METHOD_NOT_ALLOWED,
    HTTPD_408_REQ_TIMEOUT,
    HTTPD_411_LENGTH_REQUIRED,
    HTTPD_414_URI_TOO_LONG,
    HTTPD_431_REQ_HDR_FIELDS_TOO_LARGE,
    HTTPD_ERR_CODE_MAX
} httpd_err_code_t;

typedef esp_err_t (*httpd_err_handler_func_t)(httpd_req_t *req, httpd_err_code_t erro);
typedef void (*httpd_work_fn_t)(void *arg);

esp_err_t httpd_start(httpd_handle_t *alca, const httpd_config_t *config);
esp_err_t httpd_stop(httpd_handle_t alca);
esp_err_t httpd_register_uri_handler(httpd_handle_t alca, const httpd_uri_t *uri);
esp_err_t httpd_unregister_uri_handler(httpd_handle_t alca, const char *uri, httpd_method_t metodo);
esp_err_t httpd_unregister_uri(httpd_handle_t alca, const char *uri);
esp_err_t httpd_register_err_handler(httpd_handle_t alca, httpd_err_code_t erro,
                                     httpd_err_handler_func_t tratador);
bool httpd_uri_match_wildcard(const char *modelo, const char *uri, size_t ate);

esp_err_t httpd_resp_send(httpd_req_t *r, const char *buf, ssize_t n);
esp_err_t httpd_resp_send_chunk(httpd_req_t *r, const char *buf, ssize_t n);
esp_err_t httpd_resp_set_status(httpd_req_t *r, const char *status);
esp_err_t httpd_resp_set_type(httpd_req_t *r, const char *tipo);
esp_err_t httpd_resp_set_hdr(httpd_req_t *r, const char *campo, const char *valor);
esp_err_t httpd_resp_send_err(httpd_req_t *r, httpd_err_code_t erro, const char *mensagem);
esp_err_t httpd_resp_send_custom_err(httpd_req_t *r, const char *status, const char *mensagem);

static inline esp_err_t httpd_resp_sendstr(httpd_req_t *r, const char *s)
{
    return httpd_resp_send(r, s, (s == NULL) ? 0 : HTTPD_RESP_USE_STRLEN);
}
static inline esp_err_t httpd_resp_sendstr_chunk(httpd_req_t *r, const char *s)
{
    return httpd_resp_send_chunk(r, s, (s == NULL) ? 0 : HTTPD_RESP_USE_STRLEN);
}
static inline esp_err_t httpd_resp_send_404(httpd_req_t *r)
{
    return httpd_resp_send_err(r, HTTPD_404_NOT_FOUND, NULL);
}
static inline esp_err_t httpd_resp_send_408(httpd_req_t *r)
{
    return httpd_resp_send_err(r, HTTPD_408_REQ_TIMEOUT, NULL);
}
static inline esp_err_t httpd_resp_send_500(httpd_req_t *r)
{
    return httpd_resp_send_err(r, HTTPD_500_INTERNAL_SERVER_ERROR, NULL);
}

size_t httpd_req_get_url_query_len(httpd_req_t *r);
esp_err_t httpd_req_get_url_query_str(httpd_req_t *r, char *buf, size_t n);
esp_err_t httpd_query_key_value(const char *consulta, const char *chave, char *valor, size_t n);
size_t httpd_req_get_hdr_value_len(httpd_req_t *r, const char *campo);
esp_err_t httpd_req_get_hdr_value_str(httpd_req_t *r, const char *campo, char *valor, size_t n);
esp_err_t httpd_req_get_cookie_val(httpd_req_t *r, const char *nome, char *valor, size_t *n);
int httpd_req_recv(httpd_req_t *r, char *buf, size_t n);
int httpd_req_to_sockfd(httpd_req_t *r);
void *httpd_get_global_user_ctx(httpd_handle_t alca);
esp_err_t httpd_req_async_handler_begin(httpd_req_t *r, httpd_req_t **copia);
esp_err_t httpd_req_async_handler_complete(httpd_req_t *r);
esp_err_t httpd_queue_work(httpd_handle_t alca, httpd_work_fn_t trabalho, void *arg);
esp_err_t httpd_sess_trigger_close(httpd_handle_t alca, int sockfd);

#endif
