/* cs_rede.c -- rede: o laco de eventos, a interface (esp_netif), o WiFi e
 * o servidor HTTP do ESP-IDF.
 *
 * O WiFi e o da bancada (as redes do painel Rede); o IP e o que ela da.
 * O servidor HTTP abre uma ponte em localhost para o navegador do
 * computador, como os servidores do Arduino ja fazem no simulador. */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <ctype.h>
#include "cs_host.h"
#include "cs_interno.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_event.h"
#include "esp_netif.h"
#include "esp_wifi.h"
#include "esp_http_server.h"

ESP_EVENT_DEFINE_BASE(WIFI_EVENT);
ESP_EVENT_DEFINE_BASE(IP_EVENT);

/* ---------------------------------------------------------------------
 * laco de eventos */
#define DADOS_DO_EVENTO 160

typedef struct {
    esp_event_base_t base;
    int32_t id;
    int n;
    uint8_t dados[DADOS_DO_EVENTO];
} evento_t;

typedef struct tratador_t {
    esp_event_base_t base;
    int32_t id;
    esp_event_handler_t funcao;
    void *arg;
    int apagado;
    struct tratador_t *proximo;
} tratador_t;

static QueueHandle_t fila_de_eventos;
static tratador_t *tratadores;
static cs_giro trava_dos_tratadores;

static int mesma_base(esp_event_base_t a, esp_event_base_t b)
{
    if (a == b) return 1;
    if (!a || !b) return 0;
    return strcmp(a, b) == 0;
}

static void tarefa_de_eventos(void *arg)
{
    (void)arg;
    evento_t ev;
    for (;;) {
        if (xQueueReceive(fila_de_eventos, &ev, portMAX_DELAY) != pdTRUE) continue;
        tratador_t *t = tratadores;
        while (t) {
            if (!t->apagado && (t->base == ESP_EVENT_ANY_BASE || mesma_base(t->base, ev.base))
                && (t->id == ESP_EVENT_ANY_ID || t->id == ev.id))
                t->funcao(t->arg, ev.base, ev.id, ev.n ? ev.dados : NULL);
            t = t->proximo;
        }
    }
}

esp_err_t esp_event_loop_create_default(void)
{
    if (fila_de_eventos) return ESP_ERR_INVALID_STATE;
    fila_de_eventos = xQueueCreate(32, sizeof(evento_t));
    if (!fila_de_eventos) return ESP_ERR_NO_MEM;
    if (xTaskCreatePinnedToCore(tarefa_de_eventos, "sys_evt", 4096, NULL, 20, NULL, 0) != pdPASS)
        return ESP_ERR_NO_MEM;
    return ESP_OK;
}

esp_err_t esp_event_loop_delete_default(void) { return ESP_OK; }

esp_err_t esp_event_loop_create(const esp_event_loop_args_t *args, esp_event_loop_handle_t *laco)
{
    (void)args;
    if (laco) *laco = (esp_event_loop_handle_t)1;
    return esp_event_loop_create_default() == ESP_ERR_NO_MEM ? ESP_ERR_NO_MEM : ESP_OK;
}

esp_err_t esp_event_loop_delete(esp_event_loop_handle_t laco) { (void)laco; return ESP_OK; }
esp_err_t esp_event_loop_run(esp_event_loop_handle_t laco, TickType_t espera) { (void)laco; vTaskDelay(espera); return ESP_OK; }

static esp_err_t registrar(esp_event_base_t base, int32_t id, esp_event_handler_t funcao, void *arg,
                           esp_event_handler_instance_t *instancia)
{
    if (!funcao) return ESP_ERR_INVALID_ARG;
    tratador_t *t = (tratador_t *)malloc(sizeof(tratador_t));
    if (!t) return ESP_ERR_NO_MEM;
    t->base = base;
    t->id = id;
    t->funcao = funcao;
    t->arg = arg;
    t->apagado = 0;
    cs_girar(&trava_dos_tratadores);
    t->proximo = NULL;
    tratador_t **p = &tratadores;
    while (*p) p = &(*p)->proximo;
    *p = t;
    cs_soltar(&trava_dos_tratadores);
    if (instancia) *instancia = t;
    return ESP_OK;
}

esp_err_t esp_event_handler_register(esp_event_base_t base, int32_t id, esp_event_handler_t funcao, void *arg)
{
    return registrar(base, id, funcao, arg, NULL);
}

esp_err_t esp_event_handler_instance_register(esp_event_base_t base, int32_t id, esp_event_handler_t funcao,
                                              void *arg, esp_event_handler_instance_t *instancia)
{
    return registrar(base, id, funcao, arg, instancia);
}

esp_err_t esp_event_handler_register_with(esp_event_loop_handle_t laco, esp_event_base_t base, int32_t id,
                                          esp_event_handler_t funcao, void *arg)
{
    (void)laco;
    return registrar(base, id, funcao, arg, NULL);
}

esp_err_t esp_event_handler_instance_register_with(esp_event_loop_handle_t laco, esp_event_base_t base, int32_t id,
                                                   esp_event_handler_t funcao, void *arg,
                                                   esp_event_handler_instance_t *instancia)
{
    (void)laco;
    return registrar(base, id, funcao, arg, instancia);
}

esp_err_t esp_event_handler_unregister(esp_event_base_t base, int32_t id, esp_event_handler_t funcao)
{
    for (tratador_t *t = tratadores; t; t = t->proximo)
        if (mesma_base(t->base, base) && t->id == id && t->funcao == funcao) t->apagado = 1;
    return ESP_OK;
}

esp_err_t esp_event_handler_instance_unregister(esp_event_base_t base, int32_t id,
                                                esp_event_handler_instance_t instancia)
{
    (void)base; (void)id;
    if (instancia) ((tratador_t *)instancia)->apagado = 1;
    return ESP_OK;
}

esp_err_t esp_event_post(esp_event_base_t base, int32_t id, const void *dados, size_t n, TickType_t espera)
{
    if (!fila_de_eventos) return ESP_ERR_INVALID_STATE;
    evento_t ev;
    memset(&ev, 0, sizeof(ev));
    ev.base = base;
    ev.id = id;
    ev.n = (int)(n > DADOS_DO_EVENTO ? DADOS_DO_EVENTO : n);
    if (dados && ev.n) memcpy(ev.dados, dados, (size_t)ev.n);
    return xQueueSend(fila_de_eventos, &ev, espera) == pdTRUE ? ESP_OK : ESP_ERR_TIMEOUT;
}

esp_err_t esp_event_post_to(esp_event_loop_handle_t laco, esp_event_base_t base, int32_t id, const void *dados,
                            size_t n, TickType_t espera)
{
    (void)laco;
    return esp_event_post(base, id, dados, n, espera);
}

esp_err_t esp_event_isr_post(esp_event_base_t base, int32_t id, const void *dados, size_t n, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return esp_event_post(base, id, dados, n, 0);
}

/* ---------------------------------------------------------------------
 * interface de rede */
struct esp_netif_obj {
    int ap;
    char nome[32];
};

static struct esp_netif_obj interface_sta = { 0, "espressif" };
static struct esp_netif_obj interface_ap = { 1, "espressif" };
static int netif_iniciado;

esp_err_t esp_netif_init(void)
{
    netif_iniciado = 1;
    return ESP_OK;
}

esp_err_t esp_netif_deinit(void) { return ESP_ERR_NOT_SUPPORTED; }
esp_netif_t *esp_netif_create_default_wifi_sta(void) { return &interface_sta; }
esp_netif_t *esp_netif_create_default_wifi_ap(void) { return &interface_ap; }
void esp_netif_destroy(esp_netif_t *netif) { (void)netif; }
void esp_netif_destroy_default_wifi(void *netif) { (void)netif; }

esp_err_t esp_netif_get_ip_info(esp_netif_t *netif, esp_netif_ip_info_t *info)
{
    (void)netif;
    if (!info) return ESP_ERR_INVALID_ARG;
    uint8_t ip[4], mascara[4], gw[4];
    memset(info, 0, sizeof(*info));
    if (cs->wifi_ip(ip, mascara, gw)) {
        info->ip.addr = ESP_IP4TOADDR(ip[0], ip[1], ip[2], ip[3]);
        info->netmask.addr = ESP_IP4TOADDR(mascara[0], mascara[1], mascara[2], mascara[3]);
        info->gw.addr = ESP_IP4TOADDR(gw[0], gw[1], gw[2], gw[3]);
    }
    return ESP_OK;
}

esp_err_t esp_netif_set_hostname(esp_netif_t *netif, const char *nome)
{
    if (!netif || !nome) return ESP_ERR_INVALID_ARG;
    strncpy(netif->nome, nome, sizeof(netif->nome) - 1);
    return ESP_OK;
}

esp_err_t esp_netif_get_hostname(esp_netif_t *netif, const char **nome)
{
    if (!netif || !nome) return ESP_ERR_INVALID_ARG;
    *nome = netif->nome;
    return ESP_OK;
}

esp_err_t esp_netif_get_mac(esp_netif_t *netif, uint8_t mac[])
{
    cs->wifi_mac(mac);
    if (netif && netif->ap) mac[5] += 1;
    return ESP_OK;
}

esp_netif_t *esp_netif_get_handle_from_ifkey(const char *chave)
{
    if (!chave) return NULL;
    if (strcmp(chave, "WIFI_STA_DEF") == 0) return &interface_sta;
    if (strcmp(chave, "WIFI_AP_DEF") == 0) return &interface_ap;
    return NULL;
}

bool esp_netif_is_netif_up(esp_netif_t *netif)
{
    return netif == &interface_sta && cs->wifi_estado() == CS_WIFI_CONECTADO;
}

char *esp_ip4addr_ntoa(const esp_ip4_addr_t *addr, char *buf, int n)
{
    snprintf(buf, (size_t)n, IPSTR, IP2STR(addr));
    return buf;
}

uint32_t esp_ip4addr_aton(const char *texto)
{
    unsigned a = 0, b = 0, c = 0, d = 0;
    if (sscanf(texto, "%u.%u.%u.%u", &a, &b, &c, &d) != 4) return 0;
    return ESP_IP4TOADDR(a, b, c, d);
}

esp_err_t esp_netif_str_to_ip4(const char *texto, esp_ip4_addr_t *addr)
{
    addr->addr = esp_ip4addr_aton(texto);
    return addr->addr ? ESP_OK : ESP_FAIL;
}

/* ---------------------------------------------------------------------
 * WiFi: uma tarefa vigia o estado na bancada e manda os eventos */
static int wifi_iniciado, wifi_ligado;
static wifi_mode_t modo_do_wifi = WIFI_MODE_NULL;
static wifi_config_t config_sta, config_ap;
static volatile int quer_conectar;
static int vigia_criada;

static void postar_desconexao(int motivo)
{
    wifi_event_sta_disconnected_t d;
    memset(&d, 0, sizeof(d));
    size_t n = strnlen((const char *)config_sta.sta.ssid, sizeof(config_sta.sta.ssid));
    memcpy(d.ssid, config_sta.sta.ssid, n);
    d.ssid_len = (uint8_t)n;
    d.reason = (uint8_t)motivo;
    d.rssi = -127;
    esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_DISCONNECTED, &d, sizeof(d), 0);
}

static void vigia_do_wifi(void *arg)
{
    (void)arg;
    int antes = CS_WIFI_DESLIGADO;
    for (;;) {
        int agora = cs->wifi_estado();
        if (agora != antes) {
            if (agora == CS_WIFI_CONECTADO) {
                wifi_event_sta_connected_t c;
                memset(&c, 0, sizeof(c));
                size_t n = strnlen((const char *)config_sta.sta.ssid, sizeof(config_sta.sta.ssid));
                memcpy(c.ssid, config_sta.sta.ssid, n);
                c.ssid_len = (uint8_t)n;
                c.channel = 6;
                c.authmode = config_sta.sta.password[0] ? WIFI_AUTH_WPA2_PSK : WIFI_AUTH_OPEN;
                esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_CONNECTED, &c, sizeof(c), 0);

                ip_event_got_ip_t ip;
                memset(&ip, 0, sizeof(ip));
                ip.esp_netif = &interface_sta;
                esp_netif_get_ip_info(&interface_sta, &ip.ip_info);
                ip.ip_changed = true;
                esp_event_post(IP_EVENT, IP_EVENT_STA_GOT_IP, &ip, sizeof(ip), 0);
            } else if (agora == CS_WIFI_SEM_REDE) {
                postar_desconexao(WIFI_REASON_NO_AP_FOUND);
            } else if (agora == CS_WIFI_SENHA_ERRADA) {
                postar_desconexao(WIFI_REASON_AUTH_FAIL);
            } else if (antes == CS_WIFI_CONECTADO) {
                postar_desconexao(WIFI_REASON_BEACON_TIMEOUT);
            }
            antes = agora;
        }
        vTaskDelay(pdMS_TO_TICKS(100));
    }
}

esp_err_t esp_wifi_init(const wifi_init_config_t *config)
{
    if (!config || config->magic != WIFI_INIT_CONFIG_MAGIC) return ESP_ERR_INVALID_ARG;
    wifi_iniciado = 1;
    return ESP_OK;
}

esp_err_t esp_wifi_deinit(void)
{
    wifi_iniciado = 0;
    return ESP_OK;
}

esp_err_t esp_wifi_set_mode(wifi_mode_t modo)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    modo_do_wifi = modo;
    return ESP_OK;
}

esp_err_t esp_wifi_get_mode(wifi_mode_t *modo)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    *modo = modo_do_wifi;
    return ESP_OK;
}

esp_err_t esp_wifi_set_config(wifi_interface_t interface, wifi_config_t *config)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!config) return ESP_ERR_INVALID_ARG;
    if (interface == WIFI_IF_STA) config_sta = *config;
    else config_ap = *config;
    return ESP_OK;
}

esp_err_t esp_wifi_get_config(wifi_interface_t interface, wifi_config_t *config)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    *config = interface == WIFI_IF_STA ? config_sta : config_ap;
    return ESP_OK;
}

esp_err_t esp_wifi_set_storage(wifi_storage_t onde) { (void)onde; return ESP_OK; }

esp_err_t esp_wifi_start(void)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    wifi_ligado = 1;
    if (!vigia_criada) {
        vigia_criada = 1;
        xTaskCreatePinnedToCore(vigia_do_wifi, "wifi", 3584, NULL, 23, NULL, 0);
    }
    if (modo_do_wifi == WIFI_MODE_STA || modo_do_wifi == WIFI_MODE_APSTA)
        esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_START, NULL, 0, 0);
    if (modo_do_wifi == WIFI_MODE_AP || modo_do_wifi == WIFI_MODE_APSTA)
        esp_event_post(WIFI_EVENT, WIFI_EVENT_AP_START, NULL, 0, 0);
    return ESP_OK;
}

esp_err_t esp_wifi_stop(void)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    cs->wifi_desligar();
    wifi_ligado = 0;
    esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_STOP, NULL, 0, 0);
    return ESP_OK;
}

esp_err_t esp_wifi_connect(void)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!wifi_ligado) return ESP_ERR_WIFI_NOT_STARTED;
    char ssid[33], senha[65];
    memcpy(ssid, config_sta.sta.ssid, 32);
    ssid[32] = 0;
    memcpy(senha, config_sta.sta.password, 64);
    senha[64] = 0;
    cs->wifi_conectar(ssid, senha);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t esp_wifi_disconnect(void)
{
    if (!wifi_iniciado) return ESP_ERR_WIFI_NOT_INIT;
    cs->wifi_desligar();
    return ESP_OK;
}

esp_err_t esp_wifi_set_ps(wifi_ps_type_t tipo) { (void)tipo; return ESP_OK; }

esp_err_t esp_wifi_get_ps(wifi_ps_type_t *tipo)
{
    *tipo = WIFI_PS_MIN_MODEM;
    return ESP_OK;
}

esp_err_t esp_wifi_get_mac(wifi_interface_t interface, uint8_t mac[6])
{
    cs->wifi_mac(mac);
    if (interface == WIFI_IF_AP) mac[5] += 1;
    return ESP_OK;
}

esp_err_t esp_wifi_set_mac(wifi_interface_t interface, const uint8_t mac[6])
{
    (void)interface; (void)mac;
    return ESP_OK;
}

esp_err_t esp_wifi_sta_get_ap_info(wifi_ap_record_t *info)
{
    if (cs->wifi_estado() != CS_WIFI_CONECTADO) return 0x3005;   /* ESP_ERR_WIFI_NOT_CONNECT */
    memset(info, 0, sizeof(*info));
    memcpy(info->ssid, config_sta.sta.ssid, 32);
    info->rssi = (int8_t)cs->wifi_rssi();
    info->primary = 6;
    info->authmode = config_sta.sta.password[0] ? WIFI_AUTH_WPA2_PSK : WIFI_AUTH_OPEN;
    info->phy_11b = info->phy_11g = info->phy_11n = 1;
    return ESP_OK;
}

esp_err_t esp_wifi_sta_get_rssi(int *rssi)
{
    if (cs->wifi_estado() != CS_WIFI_CONECTADO) return 0x3005;
    *rssi = cs->wifi_rssi();
    return ESP_OK;
}

esp_err_t esp_wifi_scan_start(const wifi_scan_config_t *config, bool bloquear)
{
    (void)config;
    if (!wifi_ligado) return ESP_ERR_WIFI_NOT_STARTED;
    if (bloquear) vTaskDelay(pdMS_TO_TICKS(1500));
    wifi_event_sta_scan_done_t d = { 0, 0, 0 };
    esp_event_post(WIFI_EVENT, WIFI_EVENT_SCAN_DONE, &d, sizeof(d), 0);
    return ESP_OK;
}

esp_err_t esp_wifi_scan_stop(void) { return ESP_OK; }

esp_err_t esp_wifi_scan_get_ap_num(uint16_t *quantos)
{
    *quantos = 0;
    return ESP_OK;
}

esp_err_t esp_wifi_scan_get_ap_records(uint16_t *quantos, wifi_ap_record_t *lista)
{
    (void)lista;
    *quantos = 0;
    return ESP_OK;
}

esp_err_t esp_wifi_clear_ap_list(void) { return ESP_OK; }
esp_err_t esp_wifi_set_max_tx_power(int8_t potencia) { (void)potencia; return ESP_OK; }

esp_err_t esp_wifi_get_max_tx_power(int8_t *potencia)
{
    *potencia = 80;
    return ESP_OK;
}

/* ---------------------------------------------------------------------
 * servidor HTTP */
typedef struct {
    char uri[128];
    int metodo;
    esp_err_t (*funcao)(httpd_req_t *r);
    void *contexto;
} rota_t;

typedef struct trabalho_t {
    httpd_work_fn_t funcao;
    void *arg;
    struct trabalho_t *proximo;
} trabalho_t;

typedef struct servidor_t {
    int64_t id;
    httpd_config_t config;
    rota_t *rotas;
    int rotas_usadas;
    volatile int parar;
    httpd_err_handler_func_t erros[HTTPD_ERR_CODE_MAX];
    trabalho_t *trabalhos;
    cs_giro trava;
} servidor_t;

/* O pedido que o tratador ve e o comeco deste: da para voltar dele. */
typedef struct pedido_t {
    httpd_req_t r;
    int64_t req;
    int assincrono;
    servidor_t *servidor;
} pedido_t;

bool httpd_uri_match_wildcard(const char *modelo, const char *uri, size_t ate)
{
    size_t n = strlen(modelo);
    while (n > 0 && modelo[n - 1] == '?') {
        /* "?" no fim: o caractere de antes e opcional */
        n--;
        if (n > 0 && ate == n - 1 && strncmp(modelo, uri, ate) == 0) return true;
    }
    if (n > 0 && modelo[n - 1] == '*') {
        n--;
        return ate >= n && strncmp(modelo, uri, n) == 0;
    }
    return ate == n && strncmp(modelo, uri, n) == 0;
}

static size_t tamanho_do_caminho(const char *uri)
{
    const char *q = strchr(uri, '?');
    return q ? (size_t)(q - uri) : strlen(uri);
}

static rota_t *achar_rota(servidor_t *s, const char *uri, int metodo, int *existe_outro_metodo)
{
    size_t ate = tamanho_do_caminho(uri);
    *existe_outro_metodo = 0;
    for (int i = 0; i < s->rotas_usadas; i++) {
        rota_t *r = &s->rotas[i];
        int bate = s->config.uri_match_fn ? s->config.uri_match_fn(r->uri, uri, ate)
                                          : (strlen(r->uri) == ate && strncmp(r->uri, uri, ate) == 0);
        if (!bate) continue;
        if (r->metodo == metodo || r->metodo == HTTP_ANY) return r;
        *existe_outro_metodo = 1;
    }
    return NULL;
}

static void rodar_trabalhos(servidor_t *s)
{
    for (;;) {
        cs_girar(&s->trava);
        trabalho_t *t = s->trabalhos;
        if (t) s->trabalhos = t->proximo;
        cs_soltar(&s->trava);
        if (!t) return;
        t->funcao(t->arg);
        free(t);
    }
}

static void atender(servidor_t *s, int64_t req, cs_pedido_http *info)
{
    pedido_t *p = (pedido_t *)calloc(1, sizeof(pedido_t));
    if (!p) {
        cs->http_fim(req, ESP_FAIL);
        return;
    }
    p->req = req;
    p->servidor = s;
    p->r.handle = s;
    p->r.method = info->metodo;
    strncpy((char *)p->r.uri, info->uri, HTTPD_MAX_URI_LEN);
    p->r.content_len = (size_t)(info->tamanho_do_corpo > 0 ? info->tamanho_do_corpo : 0);
    p->r.aux = p;

    int outro_metodo;
    rota_t *rota = achar_rota(s, p->r.uri, p->r.method, &outro_metodo);
    esp_err_t resultado;
    if (rota) {
        p->r.user_ctx = rota->contexto;
        resultado = rota->funcao(&p->r);
    } else {
        httpd_err_code_t erro = outro_metodo ? HTTPD_405_METHOD_NOT_ALLOWED : HTTPD_404_NOT_FOUND;
        if (s->erros[erro]) resultado = s->erros[erro](&p->r, erro);
        else resultado = httpd_resp_send_err(&p->r, erro, NULL);
    }
    CS_CONFERIR();
    if (!p->assincrono) {
        cs->http_fim(req, resultado);
        free(p);
    }
}

static void tarefa_do_servidor(void *arg)
{
    servidor_t *s = (servidor_t *)arg;
    cs_pedido_http info;
    while (!s->parar) {
        memset(&info, 0, sizeof(info));
        int64_t req = cs->http_esperar(s->id, 50000, &info);
        CS_CONFERIR();
        if (req < 0) break;
        if (req > 0) atender(s, req, &info);
        rodar_trabalhos(s);
    }
    vTaskDelete(NULL);
}

esp_err_t httpd_start(httpd_handle_t *alca, const httpd_config_t *config)
{
    if (!alca || !config) return ESP_ERR_INVALID_ARG;
    servidor_t *s = (servidor_t *)calloc(1, sizeof(servidor_t));
    if (!s) return ESP_ERR_HTTPD_ALLOC_MEM;
    s->config = *config;
    int max = config->max_uri_handlers > 0 ? config->max_uri_handlers : 8;
    s->rotas = (rota_t *)calloc((size_t)max, sizeof(rota_t));
    if (!s->rotas) {
        free(s);
        return ESP_ERR_HTTPD_ALLOC_MEM;
    }
    s->id = cs->http_iniciar(config->server_port);
    CS_CONFERIR();
    if (s->id <= 0) {
        ESP_LOGE("httpd", "httpd_server_init: error in creating socket (%d)", (int)s->id);
        free(s->rotas);
        free(s);
        return ESP_ERR_HTTPD_TASK;
    }
    if (xTaskCreatePinnedToCore(tarefa_do_servidor, "httpd", config->stack_size, s, config->task_priority, NULL,
                                config->core_id) != pdPASS) {
        cs->http_parar(s->id);
        free(s->rotas);
        free(s);
        return ESP_ERR_HTTPD_TASK;
    }
    *alca = s;
    return ESP_OK;
}

esp_err_t httpd_stop(httpd_handle_t alca)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s) return ESP_ERR_INVALID_ARG;
    s->parar = 1;
    cs->http_parar(s->id);
    return ESP_OK;
}

esp_err_t httpd_register_uri_handler(httpd_handle_t alca, const httpd_uri_t *uri)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s || !uri || !uri->uri || !uri->handler) return ESP_ERR_INVALID_ARG;
    int max = s->config.max_uri_handlers > 0 ? s->config.max_uri_handlers : 8;
    for (int i = 0; i < s->rotas_usadas; i++)
        if (strcmp(s->rotas[i].uri, uri->uri) == 0 && s->rotas[i].metodo == (int)uri->method) {
            ESP_LOGW("httpd_uri", "handler %s with method %d already registered", uri->uri, uri->method);
            return ESP_ERR_HTTPD_HANDLER_EXISTS;
        }
    if (s->rotas_usadas >= max) {
        ESP_LOGW("httpd_uri", "no slots left for registering handler");
        return ESP_ERR_HTTPD_HANDLERS_FULL;
    }
    rota_t *r = &s->rotas[s->rotas_usadas++];
    strncpy(r->uri, uri->uri, sizeof(r->uri) - 1);
    r->metodo = uri->method;
    r->funcao = uri->handler;
    r->contexto = uri->user_ctx;
    return ESP_OK;
}

esp_err_t httpd_unregister_uri_handler(httpd_handle_t alca, const char *uri, httpd_method_t metodo)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s || !uri) return ESP_ERR_INVALID_ARG;
    for (int i = 0; i < s->rotas_usadas; i++)
        if (strcmp(s->rotas[i].uri, uri) == 0 && s->rotas[i].metodo == (int)metodo) {
            s->rotas[i] = s->rotas[--s->rotas_usadas];
            return ESP_OK;
        }
    return ESP_ERR_NOT_FOUND;
}

esp_err_t httpd_unregister_uri(httpd_handle_t alca, const char *uri)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s || !uri) return ESP_ERR_INVALID_ARG;
    int achou = 0;
    for (int i = 0; i < s->rotas_usadas;)
        if (strcmp(s->rotas[i].uri, uri) == 0) {
            s->rotas[i] = s->rotas[--s->rotas_usadas];
            achou = 1;
        } else {
            i++;
        }
    return achou ? ESP_OK : ESP_ERR_NOT_FOUND;
}

esp_err_t httpd_register_err_handler(httpd_handle_t alca, httpd_err_code_t erro, httpd_err_handler_func_t tratador)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s || erro >= HTTPD_ERR_CODE_MAX) return ESP_ERR_INVALID_ARG;
    s->erros[erro] = tratador;
    return ESP_OK;
}

static int64_t req_de(httpd_req_t *r)
{
    return r ? ((pedido_t *)r->aux)->req : 0;
}

esp_err_t httpd_resp_set_status(httpd_req_t *r, const char *status)
{
    if (!r || !status) return ESP_ERR_INVALID_ARG;
    cs->http_status(req_de(r), status);
    return ESP_OK;
}

esp_err_t httpd_resp_set_type(httpd_req_t *r, const char *tipo)
{
    if (!r || !tipo) return ESP_ERR_INVALID_ARG;
    cs->http_tipo(req_de(r), tipo);
    return ESP_OK;
}

esp_err_t httpd_resp_set_hdr(httpd_req_t *r, const char *campo, const char *valor)
{
    if (!r || !campo || !valor) return ESP_ERR_INVALID_ARG;
    return cs->http_cabecalho(req_de(r), campo, valor) == 0 ? ESP_OK : ESP_ERR_HTTPD_RESP_HDR;
}

esp_err_t httpd_resp_send(httpd_req_t *r, const char *buf, ssize_t n)
{
    if (!r) return ESP_ERR_INVALID_ARG;
    if (n == HTTPD_RESP_USE_STRLEN) n = buf ? (ssize_t)strlen(buf) : 0;
    int e = cs->http_enviar(req_de(r), buf, (int)n);
    CS_CONFERIR();
    return e == 0 ? ESP_OK : ESP_ERR_HTTPD_RESP_SEND;
}

esp_err_t httpd_resp_send_chunk(httpd_req_t *r, const char *buf, ssize_t n)
{
    if (!r) return ESP_ERR_INVALID_ARG;
    if (n == HTTPD_RESP_USE_STRLEN) n = buf ? (ssize_t)strlen(buf) : 0;
    if (!buf) n = 0;
    int e = cs->http_pedaco(req_de(r), buf, (int)n);
    CS_CONFERIR();
    return e == 0 ? ESP_OK : ESP_ERR_HTTPD_RESP_SEND;
}

static const char *status_do_erro(httpd_err_code_t erro, const char **texto)
{
    switch (erro) {
    case HTTPD_501_METHOD_NOT_IMPLEMENTED: *texto = "Request method is not supported by server"; return "501 Method Not Implemented";
    case HTTPD_505_VERSION_NOT_SUPPORTED: *texto = "HTTP version not supported by server"; return "505 Version Not Supported";
    case HTTPD_400_BAD_REQUEST: *texto = "Bad request syntax"; return "400 Bad Request";
    case HTTPD_401_UNAUTHORIZED: *texto = "No permission -- see authorization schemes"; return "401 Unauthorized";
    case HTTPD_403_FORBIDDEN: *texto = "Request forbidden -- authorization will not help"; return "403 Forbidden";
    case HTTPD_404_NOT_FOUND: *texto = "Nothing matches the given URI"; return "404 Not Found";
    case HTTPD_405_METHOD_NOT_ALLOWED: *texto = "Specified method is invalid for this resource"; return "405 Method Not Allowed";
    case HTTPD_408_REQ_TIMEOUT: *texto = "Server closed this connection"; return "408 Request Timeout";
    case HTTPD_411_LENGTH_REQUIRED: *texto = "Client must specify Content-Length"; return "411 Length Required";
    case HTTPD_414_URI_TOO_LONG: *texto = "URI is too long"; return "414 URI Too Long";
    case HTTPD_431_REQ_HDR_FIELDS_TOO_LARGE: *texto = "Header fields are too long"; return "431 Request Header Fields Too Large";
    default: *texto = "Server has encountered an unexpected error"; return "500 Internal Server Error";
    }
}

esp_err_t httpd_resp_send_err(httpd_req_t *r, httpd_err_code_t erro, const char *mensagem)
{
    const char *padrao;
    const char *status = status_do_erro(erro, &padrao);
    httpd_resp_set_status(r, status);
    httpd_resp_set_type(r, HTTPD_TYPE_TEXT);
    esp_err_t e = httpd_resp_send(r, mensagem ? mensagem : padrao, HTTPD_RESP_USE_STRLEN);
    return e == ESP_OK ? ESP_FAIL : e;
}

esp_err_t httpd_resp_send_custom_err(httpd_req_t *r, const char *status, const char *mensagem)
{
    httpd_resp_set_status(r, status);
    httpd_resp_set_type(r, HTTPD_TYPE_TEXT);
    return httpd_resp_send(r, mensagem ? mensagem : "", HTTPD_RESP_USE_STRLEN);
}

size_t httpd_req_get_url_query_len(httpd_req_t *r)
{
    int n = cs->http_consulta(req_de(r), NULL, 0);
    return n > 0 ? (size_t)n : 0;
}

esp_err_t httpd_req_get_url_query_str(httpd_req_t *r, char *buf, size_t n)
{
    if (!r || !buf || n == 0) return ESP_ERR_INVALID_ARG;
    int tamanho = cs->http_consulta(req_de(r), buf, (int)n);
    if (tamanho < 0) return ESP_ERR_NOT_FOUND;
    return (size_t)tamanho >= n ? ESP_ERR_HTTPD_RESULT_TRUNC : ESP_OK;
}

esp_err_t httpd_query_key_value(const char *consulta, const char *chave, char *valor, size_t n)
{
    if (!consulta || !chave || !valor) return ESP_ERR_INVALID_ARG;
    size_t k = strlen(chave);
    const char *p = consulta;
    while (*p) {
        const char *fim = strchr(p, '&');
        size_t tam = fim ? (size_t)(fim - p) : strlen(p);
        if (tam > k && strncmp(p, chave, k) == 0 && p[k] == '=') {
            const char *v = p + k + 1;
            size_t tv = tam - k - 1;
            if (n == 0) return ESP_ERR_HTTPD_RESULT_TRUNC;
            size_t copia = tv < n - 1 ? tv : n - 1;
            memcpy(valor, v, copia);
            valor[copia] = 0;
            return tv >= n ? ESP_ERR_HTTPD_RESULT_TRUNC : ESP_OK;
        }
        if (!fim) break;
        p = fim + 1;
    }
    return ESP_ERR_NOT_FOUND;
}

size_t httpd_req_get_hdr_value_len(httpd_req_t *r, const char *campo)
{
    int n = cs->http_ler_cabecalho(req_de(r), campo, NULL, 0);
    return n > 0 ? (size_t)n : 0;
}

esp_err_t httpd_req_get_hdr_value_str(httpd_req_t *r, const char *campo, char *valor, size_t n)
{
    if (!r || !campo || !valor || n == 0) return ESP_ERR_INVALID_ARG;
    int tamanho = cs->http_ler_cabecalho(req_de(r), campo, valor, (int)n);
    if (tamanho < 0) return ESP_ERR_NOT_FOUND;
    return (size_t)tamanho >= n ? ESP_ERR_HTTPD_RESULT_TRUNC : ESP_OK;
}

esp_err_t httpd_req_get_cookie_val(httpd_req_t *r, const char *nome, char *valor, size_t *n)
{
    char cookies[512];
    if (httpd_req_get_hdr_value_str(r, "Cookie", cookies, sizeof(cookies)) != ESP_OK) return ESP_ERR_NOT_FOUND;
    size_t k = strlen(nome);
    char *p = cookies;
    while (*p) {
        while (*p == ' ' || *p == ';') p++;
        if (strncmp(p, nome, k) == 0 && p[k] == '=') {
            char *v = p + k + 1;
            char *fim = strchr(v, ';');
            size_t tv = fim ? (size_t)(fim - v) : strlen(v);
            if (tv + 1 > *n) {
                *n = tv + 1;
                return ESP_ERR_HTTPD_RESULT_TRUNC;
            }
            memcpy(valor, v, tv);
            valor[tv] = 0;
            *n = tv + 1;
            return ESP_OK;
        }
        char *prox = strchr(p, ';');
        if (!prox) break;
        p = prox + 1;
    }
    return ESP_ERR_NOT_FOUND;
}

int httpd_req_recv(httpd_req_t *r, char *buf, size_t n)
{
    if (!r || !buf) return HTTPD_SOCK_ERR_INVALID;
    int lidos = cs->http_ler_corpo(req_de(r), buf, (int)n);
    CS_CONFERIR();
    return lidos;
}

int httpd_req_to_sockfd(httpd_req_t *r)
{
    return (int)(req_de(r) & 0x7fffffff) + 60;
}

void *httpd_get_global_user_ctx(httpd_handle_t alca)
{
    servidor_t *s = (servidor_t *)alca;
    return s ? s->config.global_user_ctx : NULL;
}

/* O pedido sai da tarefa do servidor e vai para outra: o servidor fica
 * livre para os proximos, e a conexao continua aberta ate o complete. */
esp_err_t httpd_req_async_handler_begin(httpd_req_t *r, httpd_req_t **copia)
{
    if (!r || !copia) return ESP_ERR_INVALID_ARG;
    pedido_t *original = (pedido_t *)r->aux;
    pedido_t *p = (pedido_t *)malloc(sizeof(pedido_t));
    if (!p) return ESP_ERR_NO_MEM;
    memcpy(p, original, sizeof(pedido_t));
    p->r.aux = p;
    p->assincrono = 1;
    original->assincrono = 1;
    free(original);
    *copia = &p->r;
    return ESP_OK;
}

esp_err_t httpd_req_async_handler_complete(httpd_req_t *r)
{
    if (!r) return ESP_ERR_INVALID_ARG;
    pedido_t *p = (pedido_t *)r->aux;
    cs->http_fim(p->req, ESP_OK);
    free(p);
    return ESP_OK;
}

esp_err_t httpd_queue_work(httpd_handle_t alca, httpd_work_fn_t funcao, void *arg)
{
    servidor_t *s = (servidor_t *)alca;
    if (!s || !funcao) return ESP_ERR_INVALID_ARG;
    trabalho_t *t = (trabalho_t *)malloc(sizeof(trabalho_t));
    if (!t) return ESP_ERR_NO_MEM;
    t->funcao = funcao;
    t->arg = arg;
    t->proximo = NULL;
    cs_girar(&s->trava);
    trabalho_t **p = &s->trabalhos;
    while (*p) p = &(*p)->proximo;
    *p = t;
    cs_soltar(&s->trava);
    return ESP_OK;
}

esp_err_t httpd_sess_trigger_close(httpd_handle_t alca, int sockfd)
{
    (void)alca; (void)sockfd;
    return ESP_OK;
}
