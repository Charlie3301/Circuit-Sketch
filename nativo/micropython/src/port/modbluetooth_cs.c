/* modbluetooth_cs.c -- o bluetooth.BLE do MicroPython no radio BLE do simulador. O
 * extmod/modbluetooth.c (o do MicroPython, sem mudanca) pede a pilha por estas mp_bluetooth_*;
 * na placa quem atende e o NimBLE (extmod/nimble/modbluetooth_nimble.c), aqui e a bancada: cada
 * pedido vai como texto para o "__mpble" do simulador, e o que acontece no ar (conectou, escreveu,
 * achou na varredura) volta como texto, um evento por vez, colhido nos pontos em que o programa
 * espera -- o irq do programa roda na propria thread do Python, como no port da ESP32. */
#include "py/mpconfig.h"

#if MICROPY_PY_BLUETOOTH

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "py/runtime.h"
#include "py/mperrno.h"
#include "py/mphal.h"
#include "extmod/modbluetooth.h"
#include "port/cs_py_ponte.h"

#ifndef MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME
#define MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME "MPY ESP32"
#endif

#define ERRNO_BLUETOOTH_NOT_ACTIVE MP_ENODEV

/* O maior valor de um atributo (512) em hexadecimal, mais o cabecalho do evento. */
#define CABE_NUM_EVENTO (1280)

static bool ativo;
static uint8_t endereco[6];
static uint8_t modo_do_endereco;
static char nome_gap[32] = MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME;
static size_t tamanho_do_nome = sizeof(MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME) - 1;
/* O que o NimBLE do IDF propoe na troca de MTU (CONFIG_BT_NIMBLE_ATT_PREFERRED_MTU). */
static uint16_t mtu_preferido = 256;
#if MICROPY_PY_BLUETOOTH_ENABLE_PAIRING_BONDING
static bool com_vinculo, com_mitm;
#endif

/* Os valores das caracteristicas daqui (o que o gatts_read e o gatts_write veem). */
MP_REGISTER_ROOT_POINTER(struct _mp_map_t *cs_ble_gatts_db);

static const char digitos[] = "0123456789abcdef";

static void para_hex(const uint8_t *dados, size_t n, char *texto)
{
    for (size_t i = 0; i < n; i++) {
        texto[2 * i] = digitos[dados[i] >> 4];
        texto[2 * i + 1] = digitos[dados[i] & 15];
    }
    texto[2 * n] = 0;
}

static int valor_do_digito(char c)
{
    return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
}

/* Os bytes de um texto em hexadecimal, no proprio lugar (cabem: sao a metade). Pula os ':' e os '-'. */
static size_t de_hex(const char *texto, uint8_t *dados, size_t cabe)
{
    size_t n = 0;
    while (*texto && n < cabe) {
        if (*texto == ':' || *texto == '-') {
            texto++;
            continue;
        }
        int alto = valor_do_digito(texto[0]);
        int baixo = alto < 0 ? -1 : valor_do_digito(texto[1]);
        if (baixo < 0) break;
        dados[n++] = (uint8_t)(alto << 4 | baixo);
        texto += 2;
    }
    return n;
}

/* O pedido ao simulador. Verdadeiro se ele atendeu (a resposta nao veio vazia). */
static bool pedir(const char *op, const char *texto, long a, long b, long c)
{
    char volta[32];
    return cs_py_ble(op, texto, a, b, c, volta, sizeof(volta)) > 0;
}

/* O mesmo, com bytes (em hexadecimal; nulo: "-", o que ja esta la). */
static bool pedir_com_dados(const char *op, const uint8_t *dados, size_t n, long a, long b, long c)
{
    if (!dados) return pedir(op, "-", a, b, c);
    char *texto = malloc(2 * n + 1);
    if (!texto) return false;
    para_hex(dados, n, texto);
    bool ok = pedir(op, texto, a, b, c);
    free(texto);
    return ok;
}

/* O UUID como o simulador escreve: o de 16 bits com quatro digitos, o resto inteiro. */
static void uuid_para_texto(const mp_obj_bluetooth_uuid_t *uuid, char texto[40])
{
    if (!uuid) {
        texto[0] = 0;
        return;
    }
    int n = uuid->type == MP_BLUETOOTH_UUID_TYPE_16 ? 2 : uuid->type == MP_BLUETOOTH_UUID_TYPE_32 ? 4 : 16;
    for (int i = 0; i < n; i++) {
        uint8_t b = uuid->data[n - 1 - i];   /* guardado ao contrario */
        texto[2 * i] = digitos[b >> 4];
        texto[2 * i + 1] = digitos[b & 15];
    }
    texto[2 * n] = 0;
}

static void texto_para_uuid(const char *texto, mp_obj_bluetooth_uuid_t *uuid)
{
    uint8_t bytes[16];
    size_t n = de_hex(texto, bytes, sizeof(bytes));
    uuid->base.type = &mp_type_bluetooth_uuid;
    uuid->type = n == 2 ? MP_BLUETOOTH_UUID_TYPE_16 : n == 4 ? MP_BLUETOOTH_UUID_TYPE_32 : MP_BLUETOOTH_UUID_TYPE_128;
    memset(uuid->data, 0, sizeof(uuid->data));
    for (size_t i = 0; i < n; i++) uuid->data[n - 1 - i] = bytes[i];
}

/* ---- ligar e desligar ---- */

int mp_bluetooth_init(void)
{
    mp_bluetooth_deinit();
    char mac[32];
    if (cs_py_ble("ligar", "", 0, 0, 0, mac, sizeof(mac)) <= 0) return MP_ENODEV;
    de_hex(mac, endereco, sizeof(endereco));
    mp_bluetooth_gatts_db_create(&MP_STATE_PORT(cs_ble_gatts_db));
    mp_bluetooth_gatts_db_reset(MP_STATE_PORT(cs_ble_gatts_db));
    /* Cada active(True) volta ao nome de fabrica, como o NimBLE ao sincronizar. */
    tamanho_do_nome = sizeof(MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME) - 1;
    memcpy(nome_gap, MICROPY_PY_BLUETOOTH_DEFAULT_GAP_NAME, tamanho_do_nome + 1);
    mtu_preferido = 256;
    modo_do_endereco = MP_BLUETOOTH_ADDRESS_MODE_PUBLIC;
    ativo = true;
    return 0;
}

int mp_bluetooth_deinit(void)
{
    if (!ativo) return 0;
    ativo = false;
    pedir("desligar", "", 0, 0, 0);
    MP_STATE_PORT(cs_ble_gatts_db) = NULL;
    return 0;
}

bool mp_bluetooth_is_active(void) { return ativo; }

void mp_bluetooth_get_current_address(uint8_t *addr_type, uint8_t *addr)
{
    if (!ativo) mp_raise_OSError(ERRNO_BLUETOOTH_NOT_ACTIVE);
    *addr_type = modo_do_endereco == MP_BLUETOOTH_ADDRESS_MODE_PUBLIC ? 0 : 1;
    memcpy(addr, endereco, 6);
}

void mp_bluetooth_set_address_mode(uint8_t addr_mode)
{
    if (!ativo) mp_raise_OSError(ERRNO_BLUETOOTH_NOT_ACTIVE);
    /* A bancada enxerga a placa sempre pelo endereco dela; o modo fica so anotado. */
    modo_do_endereco = addr_mode;
}

#if MICROPY_PY_BLUETOOTH_ENABLE_PAIRING_BONDING
void mp_bluetooth_set_bonding(bool enabled) { com_vinculo = enabled; }
void mp_bluetooth_set_mitm_protection(bool enabled) { com_mitm = enabled; }
void mp_bluetooth_set_le_secure(bool enabled) { (void)enabled; }
void mp_bluetooth_set_io_capability(uint8_t capability) { (void)capability; }

int mp_bluetooth_gap_pair(uint16_t conn_handle)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    /* Na bancada o pareamento da sempre certo, sem senha (o "just works"). */
    if (!pedir("pareou", "", conn_handle, com_vinculo, 0)) return MP_ENOTCONN;
    return 0;
}

int mp_bluetooth_gap_passkey(uint16_t conn_handle, uint8_t action, mp_int_t passkey)
{
    (void)conn_handle;
    (void)action;
    (void)passkey;
    return MP_EOPNOTSUPP;
}
#endif

size_t mp_bluetooth_gap_get_device_name(const uint8_t **buf)
{
    *buf = (const uint8_t *)nome_gap;
    return tamanho_do_nome;
}

int mp_bluetooth_gap_set_device_name(const uint8_t *buf, size_t len)
{
    if (len > sizeof(nome_gap) - 1) return MP_EINVAL;
    memcpy(nome_gap, buf, len);
    nome_gap[len] = 0;
    tamanho_do_nome = len;
    if (ativo) pedir("nome", nome_gap, 0, 0, 0);
    return 0;
}

/* ---- o anuncio ---- */

int mp_bluetooth_gap_advertise_start(bool connectable, int32_t interval_us, const uint8_t *adv_data, size_t adv_data_len, const uint8_t *sr_data, size_t sr_data_len)
{
    (void)interval_us;
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    if (adv_data_len > 31 || sr_data_len > 31) return MP_EINVAL;
    /* "anuncio|resposta" em hexadecimal; "-": o que ja estava (o gap_advertise sem adv_data). */
    char texto[2 * 31 + 1 + 2 * 31 + 1];
    char *p = texto;
    if (adv_data) {
        para_hex(adv_data, adv_data_len, p);
        p += 2 * adv_data_len;
    } else {
        *p++ = '-';
    }
    *p++ = '|';
    if (sr_data) {
        para_hex(sr_data, sr_data_len, p);
    } else {
        *p++ = '-';
        *p = 0;
    }
    return pedir("anunciar", texto, connectable, 0, 0) ? 0 : MP_EIO;
}

void mp_bluetooth_gap_advertise_stop(void)
{
    if (ativo) pedir("parar_anuncio", "", 0, 0, 0);
}

/* ---- os servicos daqui ---- */

int mp_bluetooth_gatts_register_service_begin(bool append)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    if (append) return MP_EOPNOTSUPP;   /* como no NimBLE */
    mp_bluetooth_gatts_db_reset(MP_STATE_PORT(cs_ble_gatts_db));
    pedir("reg_inicio", "", 0, 0, 0);
    return 0;
}

int mp_bluetooth_gatts_register_service(mp_obj_bluetooth_uuid_t *service_uuid, mp_obj_bluetooth_uuid_t **characteristic_uuids, uint16_t *characteristic_flags, mp_obj_bluetooth_uuid_t **descriptor_uuids, uint16_t *descriptor_flags, uint8_t *num_descriptors, uint16_t *handles, size_t num_characteristics)
{
    char texto[40], volta[16];
    uuid_para_texto(service_uuid, texto);
    if (!pedir("reg_servico", texto, 0, 0, 0)) return MP_EINVAL;
    size_t indice = 0, descritor = 0;
    for (size_t i = 0; i < num_characteristics; i++) {
        uuid_para_texto(characteristic_uuids[i], texto);
        if (cs_py_ble("reg_caracteristica", texto, characteristic_flags[i], 0, 0, volta, sizeof(volta)) <= 0) return MP_EINVAL;
        handles[indice] = (uint16_t)atoi(volta);
        mp_bluetooth_gatts_db_create_entry(MP_STATE_PORT(cs_ble_gatts_db), handles[indice], MP_BLUETOOTH_DEFAULT_ATTR_LEN);
        indice++;
        for (size_t j = 0; j < num_descriptors[i]; j++, descritor++) {
            uuid_para_texto(descriptor_uuids[descritor], texto);
            if (cs_py_ble("reg_descritor", texto, descriptor_flags[descritor], 0, 0, volta, sizeof(volta)) <= 0) return MP_EINVAL;
            handles[indice] = (uint16_t)atoi(volta);
            mp_bluetooth_gatts_db_create_entry(MP_STATE_PORT(cs_ble_gatts_db), handles[indice], MP_BLUETOOTH_DEFAULT_ATTR_LEN);
            indice++;
        }
    }
    return 0;
}

int mp_bluetooth_gatts_register_service_end(void)
{
    return ativo ? 0 : ERRNO_BLUETOOTH_NOT_ACTIVE;
}

int mp_bluetooth_gatts_read(uint16_t value_handle, const uint8_t **value, size_t *value_len)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return mp_bluetooth_gatts_db_read(MP_STATE_PORT(cs_ble_gatts_db), value_handle, value, value_len);
}

int mp_bluetooth_gatts_write(uint16_t value_handle, const uint8_t *value, size_t value_len, bool send_update)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    int erro = mp_bluetooth_gatts_db_write(MP_STATE_PORT(cs_ble_gatts_db), value_handle, value, value_len);
    if (erro) return erro;
    /* O valor fica tambem na bancada: e de la que o outro lado le. */
    pedir_com_dados("valor", value ? value : (const uint8_t *)"", value_len, value_handle, 0, 0);
    if (send_update) pedir("atualizar", "", value_handle, 0, 0);
    return 0;
}

int mp_bluetooth_gatts_notify_indicate(uint16_t conn_handle, uint16_t value_handle, int gatts_op, const uint8_t *value, size_t value_len)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    if (gatts_op != MP_BLUETOOTH_GATTS_OP_NOTIFY && gatts_op != MP_BLUETOOTH_GATTS_OP_INDICATE) return MP_EINVAL;
    bool ok = pedir_com_dados("notificar", value, value_len, conn_handle, value_handle, gatts_op == MP_BLUETOOTH_GATTS_OP_INDICATE);
    return ok ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gatts_set_buffer(uint16_t value_handle, size_t len, bool append)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return mp_bluetooth_gatts_db_resize(MP_STATE_PORT(cs_ble_gatts_db), value_handle, len, append);
}

int mp_bluetooth_gap_disconnect(uint16_t conn_handle)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return pedir("desconectar", "", conn_handle, 0, 0) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_get_preferred_mtu(void)
{
    if (!ativo) mp_raise_OSError(ERRNO_BLUETOOTH_NOT_ACTIVE);
    return mtu_preferido;
}

int mp_bluetooth_set_preferred_mtu(uint16_t mtu)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    if (mtu < 23 || mtu > 527) return MP_EINVAL;
    mtu_preferido = mtu;
    return 0;
}

/* ---- a central ---- */

#if MICROPY_PY_BLUETOOTH_ENABLE_CENTRAL_MODE
int mp_bluetooth_gap_scan_start(int32_t duration_ms, int32_t interval_us, int32_t window_us, bool active_scan)
{
    (void)interval_us;
    (void)window_us;
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    pedir("parar_varredura", "", 0, 0, 0);
    return pedir("varrer", "", duration_ms, active_scan, 0) ? 0 : MP_EIO;
}

int mp_bluetooth_gap_scan_stop(void)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    pedir("parar_varredura", "", 0, 0, 0);
    return 0;
}

int mp_bluetooth_gap_peripheral_connect(uint8_t addr_type, const uint8_t *addr, int32_t duration_ms, int32_t min_conn_interval_us, int32_t max_conn_interval_us)
{
    (void)duration_ms;
    (void)min_conn_interval_us;
    (void)max_conn_interval_us;
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    /* Quem conecta para de procurar (o NimBLE nao faz as duas coisas juntas). */
    pedir("parar_varredura", "", 0, 0, 0);
    char texto[13];
    para_hex(addr, 6, texto);
    return pedir("conectar", texto, addr_type, 0, 0) ? 0 : MP_EIO;
}

int mp_bluetooth_gap_peripheral_connect_cancel(void)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    /* Aqui a conexao se resolve na hora: nao ha nenhuma a caminho para cancelar. */
    return MP_EALREADY;
}
#endif

/* ---- o cliente GATT ---- */

#if MICROPY_PY_BLUETOOTH_ENABLE_GATT_CLIENT
int mp_bluetooth_gattc_discover_primary_services(uint16_t conn_handle, const mp_obj_bluetooth_uuid_t *uuid)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    char texto[40];
    uuid_para_texto(uuid, texto);
    return pedir("desc_servicos", texto, conn_handle, 0, 0) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gattc_discover_characteristics(uint16_t conn_handle, uint16_t start_handle, uint16_t end_handle, const mp_obj_bluetooth_uuid_t *uuid)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    char texto[40];
    uuid_para_texto(uuid, texto);
    return pedir("desc_caracteristicas", texto, conn_handle, start_handle, end_handle) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gattc_discover_descriptors(uint16_t conn_handle, uint16_t start_handle, uint16_t end_handle)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return pedir("desc_descritores", "", conn_handle, start_handle, end_handle) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gattc_read(uint16_t conn_handle, uint16_t value_handle)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return pedir("ler", "", conn_handle, value_handle, 0) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gattc_write(uint16_t conn_handle, uint16_t value_handle, const uint8_t *value, size_t value_len, unsigned int mode)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    if (mode != MP_BLUETOOTH_WRITE_MODE_NO_RESPONSE && mode != MP_BLUETOOTH_WRITE_MODE_WITH_RESPONSE) return MP_EINVAL;
    return pedir_com_dados("escrever", value ? value : (const uint8_t *)"", value_len, conn_handle, value_handle, (long)mode) ? 0 : MP_ENOTCONN;
}

int mp_bluetooth_gattc_exchange_mtu(uint16_t conn_handle)
{
    if (!ativo) return ERRNO_BLUETOOTH_NOT_ACTIVE;
    return pedir("mtu", "", conn_handle, mtu_preferido, 0) ? 0 : MP_ENOTCONN;
}
#endif

/* ---- o que acontece no ar: um evento por vez, na thread do Python ---- */

static void entregar(char *ev)
{
    char *campo[8];
    int n = 0;
    for (char *p = ev; n < 8;) {
        campo[n++] = p;
        p = strchr(p, '|');
        if (!p) break;
        *p++ = 0;
    }
    for (int i = n; i < 8; i++) campo[i] = "";
    int tipo = atoi(campo[0]);
    long v1 = atol(campo[1]), v2 = atol(campo[2]), v3 = atol(campo[3]);
    /* Os bytes ficam no lugar do proprio texto (sao a metade dele). */
    uint8_t *dados;
    size_t quantos;
    switch (tipo) {
        case MP_BLUETOOTH_IRQ_CENTRAL_CONNECT:
        case MP_BLUETOOTH_IRQ_CENTRAL_DISCONNECT:
        case MP_BLUETOOTH_IRQ_PERIPHERAL_CONNECT:
        case MP_BLUETOOTH_IRQ_PERIPHERAL_DISCONNECT: {
            uint8_t de[6] = {0};
            de_hex(campo[3], de, 6);
            mp_bluetooth_gap_on_connected_disconnected((uint8_t)tipo, (uint16_t)v1, (uint8_t)v2, de);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTS_WRITE: {
            mp_bluetooth_gatts_db_entry_t *e = mp_bluetooth_gatts_db_lookup(MP_STATE_PORT(cs_ble_gatts_db), (uint16_t)v2);
            if (!e) break;
            dados = (uint8_t *)campo[3];
            quantos = de_hex(campo[3], dados, strlen(campo[3]));
            /* Como no NimBLE: o que passa do tamanho do valor (20 de fabrica) se perde; no modo
             * de acrescentar (gatts_set_buffer) vai para o fim do que ja estava. */
            size_t desde = e->append ? e->data_len : 0;
            size_t novo = quantos + desde < e->data_alloc ? quantos + desde : e->data_alloc;
            memcpy(e->data + desde, dados, novo - desde);
            e->data_len = novo;
            mp_bluetooth_gatts_on_write((uint16_t)v1, (uint16_t)v2);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTS_READ_REQUEST:
            mp_bluetooth_gatts_on_read_request((uint16_t)v1, (uint16_t)v2);
            break;
        case MP_BLUETOOTH_IRQ_GATTS_INDICATE_DONE:
            mp_bluetooth_gatts_on_indicate_complete((uint16_t)v1, (uint16_t)v2, (uint8_t)v3);
            break;
        case MP_BLUETOOTH_IRQ_MTU_EXCHANGED:
            mp_bluetooth_gatts_on_mtu_exchanged((uint16_t)v1, (uint16_t)v2);
            break;
        #if MICROPY_PY_BLUETOOTH_ENABLE_PAIRING_BONDING
        case MP_BLUETOOTH_IRQ_ENCRYPTION_UPDATE:
            mp_bluetooth_gatts_on_encryption_update((uint16_t)v1, true, com_mitm, v2 != 0, 16);
            break;
        #endif
        #if MICROPY_PY_BLUETOOTH_ENABLE_CENTRAL_MODE
        case MP_BLUETOOTH_IRQ_SCAN_RESULT: {
            uint8_t de[6] = {0};
            de_hex(campo[2], de, 6);
            dados = (uint8_t *)campo[5];
            quantos = de_hex(campo[5], dados, strlen(campo[5]));
            mp_bluetooth_gap_on_scan_result((uint8_t)v1, de, (uint8_t)v3, (int8_t)atoi(campo[4]), dados, (uint16_t)quantos);
            break;
        }
        case MP_BLUETOOTH_IRQ_SCAN_DONE:
            mp_bluetooth_gap_on_scan_complete();
            break;
        #endif
        #if MICROPY_PY_BLUETOOTH_ENABLE_GATT_CLIENT
        case MP_BLUETOOTH_IRQ_GATTC_SERVICE_RESULT: {
            mp_obj_bluetooth_uuid_t uuid;
            texto_para_uuid(campo[4], &uuid);
            mp_bluetooth_gattc_on_primary_service_result((uint16_t)v1, (uint16_t)v2, (uint16_t)v3, &uuid);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTC_CHARACTERISTIC_RESULT: {
            /* conexao | fim | valor | propriedades | uuid */
            mp_obj_bluetooth_uuid_t uuid;
            texto_para_uuid(campo[5], &uuid);
            mp_bluetooth_gattc_on_characteristic_result((uint16_t)v1, (uint16_t)v3, (uint16_t)v2, (uint8_t)atoi(campo[4]), &uuid);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTC_DESCRIPTOR_RESULT: {
            mp_obj_bluetooth_uuid_t uuid;
            texto_para_uuid(campo[3], &uuid);
            mp_bluetooth_gattc_on_descriptor_result((uint16_t)v1, (uint16_t)v2, &uuid);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTC_SERVICE_DONE:
        case MP_BLUETOOTH_IRQ_GATTC_CHARACTERISTIC_DONE:
        case MP_BLUETOOTH_IRQ_GATTC_DESCRIPTOR_DONE:
            mp_bluetooth_gattc_on_discover_complete((uint8_t)tipo, (uint16_t)v1, (uint16_t)v2);
            break;
        case MP_BLUETOOTH_IRQ_GATTC_READ_RESULT:
        case MP_BLUETOOTH_IRQ_GATTC_NOTIFY:
        case MP_BLUETOOTH_IRQ_GATTC_INDICATE: {
            dados = (uint8_t *)campo[3];
            quantos = de_hex(campo[3], dados, strlen(campo[3]));
            const uint8_t *pedaco = dados;
            uint16_t tamanho = (uint16_t)quantos;
            mp_bluetooth_gattc_on_data_available((uint8_t)tipo, (uint16_t)v1, (uint16_t)v2, &pedaco, &tamanho, 1);
            break;
        }
        case MP_BLUETOOTH_IRQ_GATTC_READ_DONE:
        case MP_BLUETOOTH_IRQ_GATTC_WRITE_DONE:
            mp_bluetooth_gattc_on_read_write_status((uint8_t)tipo, (uint16_t)v1, (uint16_t)v2, (uint16_t)v3);
            break;
        #endif
    }
}

/* Chamado onde o programa espera (o sleep, o machine.idle, o laco da linha de comando) e, no laco
 * que nao espera, de tempos em tempos pela fila de tarefas. O irq do programa roda aqui dentro. */
void cs_py_ble_colher(void)
{
    static bool colhendo;
    if (!ativo || colhendo) return;
    colhendo = true;
    mp_sched_lock();
    char *ev = malloc(CABE_NUM_EVENTO);
    for (int i = 0; ev && i < 16 && ativo; i++) {
        if (cs_py_ble("evento", "", 0, 0, 0, ev, CABE_NUM_EVENTO) <= 0) break;
        entregar(ev);
    }
    free(ev);
    mp_sched_unlock();
    colhendo = false;
}

/* O laco que nao espera (while True: pass com o irq ligado): a cada tantas voltas a colheita entra
 * na fila de tarefas do MicroPython, e roda no ponto em que as interrupcoes agendadas rodam. */
static mp_sched_node_t no_da_colheita;
static unsigned voltas;

static void colher_pela_fila(mp_sched_node_t *no)
{
    (void)no;
    cs_py_ble_colher();
}

void cs_py_ble_no_laco(void)
{
    if (!ativo || ++voltas < 4096) return;
    voltas = 0;
    mp_sched_schedule_node(&no_da_colheita, colher_pela_fila);
}

#endif /* MICROPY_PY_BLUETOOTH */
