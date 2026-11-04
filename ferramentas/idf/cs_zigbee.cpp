/* cs_zigbee.cpp -- a API do Zigbee da Espressif (esp_zb_*) no simulador.
 *
 * A pilha de verdade (a ZBOSS) e binaria e so roda no chip, entao esta camada
 * faz o trabalho dela: os cabecalhos e a biblioteca do core (ZigbeeCore,
 * ZigbeeEP, ep/...) sao os de verdade, e o que muda e o que passa no ar.
 *
 * O modelo de dados e o do ZCL: endpoints, clusters e atributos com os tipos da
 * especificacao, o binding, a leitura e a escrita, os relatorios e os comandos
 * de on/off, nivel, cor e IAS. A rede e a da bancada: o coordenador forma a PAN,
 * os outros entram (beacon, join, device announce) e os sinais chegam ao
 * esp_zb_app_signal_handler na mesma ordem da placa. Os quadros sao do
 * simulador, pelo radio 802.15.4 da bancada (motor/Rede154.cs), sem seguranca
 * de rede e sem rotas: toda placa alcanca toda placa. A memoria nao persiste: a
 * placa reiniciada volta como nova de fabrica. */
#include "Arduino.h"
#include "cs_objeto.h"
#include "esp_zigbee_core.h"
#include "esp_zigbee_type.h"
#include "aps/esp_zigbee_aps.h"
#include "platform/esp_zigbee_platform.h"
#include "ha/esp_zigbee_ha_standard.h"
#include "zdo/esp_zigbee_zdo_common.h"
#include "zcl/esp_zigbee_zcl_command.h"
extern "C" {
#include "zboss_api.h"
}
#include "esp_system.h"
#include "esp_random.h"
#include "freertos/semphr.h"
#include <map>
#include <vector>
#include <string>

extern "C" void esp_zb_app_signal_handler(esp_zb_app_signal_t *signal_struct);

namespace {

/* =====================================================================
 * os tipos dos atributos (a tabela do ZCL, para os que o sketch passa so o valor) */
struct TipoDoAtributo {
    uint16_t cluster, atributo;
    uint8_t tipo;
};

#define T_U8 ESP_ZB_ZCL_ATTR_TYPE_U8
#define T_U16 ESP_ZB_ZCL_ATTR_TYPE_U16
#define T_U32 ESP_ZB_ZCL_ATTR_TYPE_U32
#define T_S8 ESP_ZB_ZCL_ATTR_TYPE_S8
#define T_S16 ESP_ZB_ZCL_ATTR_TYPE_S16
#define T_S32 ESP_ZB_ZCL_ATTR_TYPE_S32
#define T_BOOL ESP_ZB_ZCL_ATTR_TYPE_BOOL
#define T_E8 ESP_ZB_ZCL_ATTR_TYPE_8BIT_ENUM
#define T_M8 ESP_ZB_ZCL_ATTR_TYPE_8BITMAP
#define T_M16 ESP_ZB_ZCL_ATTR_TYPE_16BITMAP
#define T_F ESP_ZB_ZCL_ATTR_TYPE_SINGLE
#define T_STR ESP_ZB_ZCL_ATTR_TYPE_CHAR_STRING
#define T_ARR ESP_ZB_ZCL_ATTR_TYPE_ARRAY
#define T_UTC ESP_ZB_ZCL_ATTR_TYPE_UTC_TIME
#define T_IEEE ESP_ZB_ZCL_ATTR_TYPE_IEEE_ADDR

#define C_BASIC ESP_ZB_ZCL_CLUSTER_ID_BASIC
#define C_POWER ESP_ZB_ZCL_CLUSTER_ID_POWER_CONFIG
#define C_IDENT ESP_ZB_ZCL_CLUSTER_ID_IDENTIFY
#define C_GROUPS ESP_ZB_ZCL_CLUSTER_ID_GROUPS
#define C_SCENES ESP_ZB_ZCL_CLUSTER_ID_SCENES
#define C_ONOFF ESP_ZB_ZCL_CLUSTER_ID_ON_OFF
#define C_LEVEL ESP_ZB_ZCL_CLUSTER_ID_LEVEL_CONTROL
#define C_TIME ESP_ZB_ZCL_CLUSTER_ID_TIME
#define C_AIN ESP_ZB_ZCL_CLUSTER_ID_ANALOG_INPUT
#define C_AOUT ESP_ZB_ZCL_CLUSTER_ID_ANALOG_OUTPUT
#define C_BIN ESP_ZB_ZCL_CLUSTER_ID_BINARY_INPUT
#define C_BOUT ESP_ZB_ZCL_CLUSTER_ID_BINARY_OUTPUT
#define C_MIN ESP_ZB_ZCL_CLUSTER_ID_MULTI_INPUT
#define C_MOUT ESP_ZB_ZCL_CLUSTER_ID_MULTI_OUTPUT
#define C_WIN ESP_ZB_ZCL_CLUSTER_ID_WINDOW_COVERING
#define C_THERM ESP_ZB_ZCL_CLUSTER_ID_THERMOSTAT
#define C_FAN ESP_ZB_ZCL_CLUSTER_ID_FAN_CONTROL
#define C_COLOR ESP_ZB_ZCL_CLUSTER_ID_COLOR_CONTROL
#define C_LUX ESP_ZB_ZCL_CLUSTER_ID_ILLUMINANCE_MEASUREMENT
#define C_TEMP ESP_ZB_ZCL_CLUSTER_ID_TEMP_MEASUREMENT
#define C_PRESS ESP_ZB_ZCL_CLUSTER_ID_PRESSURE_MEASUREMENT
#define C_FLOW ESP_ZB_ZCL_CLUSTER_ID_FLOW_MEASUREMENT
#define C_HUM ESP_ZB_ZCL_CLUSTER_ID_REL_HUMIDITY_MEASUREMENT
#define C_OCC ESP_ZB_ZCL_CLUSTER_ID_OCCUPANCY_SENSING
#define C_IAS ESP_ZB_ZCL_CLUSTER_ID_IAS_ZONE
#define C_ELEC ESP_ZB_ZCL_CLUSTER_ID_ELECTRICAL_MEASUREMENT
#define C_CO2 ESP_ZB_ZCL_CLUSTER_ID_CARBON_DIOXIDE_MEASUREMENT
#define C_PM25 ESP_ZB_ZCL_CLUSTER_ID_PM2_5_MEASUREMENT
#define C_WIND ESP_ZB_ZCL_CLUSTER_ID_WIND_SPEED_MEASUREMENT
#define C_OTA ESP_ZB_ZCL_CLUSTER_ID_OTA_UPGRADE

/* Pelos numeros da especificacao do ZCL: os nomes do cabecalho variam de cluster para cluster. */
const TipoDoAtributo TIPOS[] = {
    {C_BASIC, 0x0000, T_U8}, {C_BASIC, 0x0001, T_U8}, {C_BASIC, 0x0002, T_U8}, {C_BASIC, 0x0003, T_U8}, {C_BASIC, 0x0004, T_STR}, {C_BASIC, 0x0005, T_STR},
    {C_BASIC, 0x0006, T_STR}, {C_BASIC, 0x0007, T_E8}, {C_BASIC, 0x0010, T_STR}, {C_BASIC, 0x0011, T_E8}, {C_BASIC, 0x0012, T_BOOL}, {C_BASIC, 0x0013, T_M8},
    {C_BASIC, 0x4000, T_STR},
    {C_POWER, 0x0000, T_U16}, {C_POWER, 0x0001, T_U8}, {C_POWER, 0x0020, T_U8}, {C_POWER, 0x0021, T_U8}, {C_POWER, 0x0031, T_E8}, {C_POWER, 0x0033, T_U8},
    {C_POWER, 0x0034, T_U8}, {C_POWER, 0x0035, T_M8}, {C_POWER, 0x003E, ESP_ZB_ZCL_ATTR_TYPE_32BITMAP},
    {C_IDENT, 0x0000, T_U16},
    {C_GROUPS, 0x0000, T_M8},
    {C_SCENES, 0x0000, T_U8}, {C_SCENES, 0x0001, T_U8}, {C_SCENES, 0x0002, T_U16}, {C_SCENES, 0x0003, T_BOOL}, {C_SCENES, 0x0004, T_M8},
    {C_ONOFF, 0x0000, T_BOOL}, {C_ONOFF, 0x4000, T_BOOL}, {C_ONOFF, 0x4001, T_U16}, {C_ONOFF, 0x4002, T_U16}, {C_ONOFF, 0x4003, T_E8},
    {C_LEVEL, 0x0000, T_U8}, {C_LEVEL, 0x0001, T_U16}, {C_LEVEL, 0x0002, T_U8}, {C_LEVEL, 0x0003, T_U8}, {C_LEVEL, 0x000F, T_M8}, {C_LEVEL, 0x0010, T_U16},
    {C_LEVEL, 0x0011, T_U8}, {C_LEVEL, 0x4000, T_U8},
    {C_TIME, 0x0000, T_UTC}, {C_TIME, 0x0001, T_M8}, {C_TIME, 0x0002, T_S32}, {C_TIME, 0x0003, T_U32}, {C_TIME, 0x0004, T_U32}, {C_TIME, 0x0005, T_S32},
    {C_TIME, 0x0006, T_U32}, {C_TIME, 0x0007, T_U32}, {C_TIME, 0x0008, T_UTC}, {C_TIME, 0x0009, T_UTC},
    {C_AIN, 0x001C, T_STR}, {C_AIN, 0x0041, T_F}, {C_AIN, 0x0045, T_F}, {C_AIN, 0x0051, T_BOOL}, {C_AIN, 0x0055, T_F}, {C_AIN, 0x0067, T_E8}, {C_AIN, 0x006A, T_F},
    {C_AIN, 0x006F, T_M8}, {C_AIN, 0x0075, ESP_ZB_ZCL_ATTR_TYPE_16BIT_ENUM}, {C_AIN, 0x0100, T_U32},
    {C_AOUT, 0x001C, T_STR}, {C_AOUT, 0x0041, T_F}, {C_AOUT, 0x0045, T_F}, {C_AOUT, 0x0051, T_BOOL}, {C_AOUT, 0x0055, T_F}, {C_AOUT, 0x0067, T_E8}, {C_AOUT, 0x0068, T_F},
    {C_AOUT, 0x006A, T_F}, {C_AOUT, 0x006F, T_M8}, {C_AOUT, 0x0075, ESP_ZB_ZCL_ATTR_TYPE_16BIT_ENUM}, {C_AOUT, 0x0100, T_U32},
    {C_BIN, 0x0004, T_STR}, {C_BIN, 0x001C, T_STR}, {C_BIN, 0x002E, T_STR}, {C_BIN, 0x0051, T_BOOL}, {C_BIN, 0x0054, T_E8}, {C_BIN, 0x0055, T_BOOL}, {C_BIN, 0x0067, T_E8},
    {C_BIN, 0x006F, T_M8}, {C_BIN, 0x0100, T_U32},
    {C_BOUT, 0x0004, T_STR}, {C_BOUT, 0x001C, T_STR}, {C_BOUT, 0x002E, T_STR}, {C_BOUT, 0x0051, T_BOOL}, {C_BOUT, 0x0054, T_E8}, {C_BOUT, 0x0055, T_BOOL},
    {C_BOUT, 0x0067, T_E8}, {C_BOUT, 0x0068, T_BOOL}, {C_BOUT, 0x006F, T_M8}, {C_BOUT, 0x0100, T_U32},
    {C_MIN, 0x000E, T_ARR}, {C_MIN, 0x001C, T_STR}, {C_MIN, 0x004A, T_U16}, {C_MIN, 0x0051, T_BOOL}, {C_MIN, 0x0055, T_U16}, {C_MIN, 0x0067, T_E8}, {C_MIN, 0x006F, T_M8},
    {C_MIN, 0x0100, T_U32},
    {C_MOUT, 0x000E, T_ARR}, {C_MOUT, 0x001C, T_STR}, {C_MOUT, 0x004A, T_U16}, {C_MOUT, 0x0051, T_BOOL}, {C_MOUT, 0x0055, T_U16}, {C_MOUT, 0x0067, T_E8},
    {C_MOUT, 0x0068, T_U16}, {C_MOUT, 0x006F, T_M8}, {C_MOUT, 0x0100, T_U32},
    {C_WIN, 0x0000, T_E8}, {C_WIN, 0x0001, T_U16}, {C_WIN, 0x0002, T_U16}, {C_WIN, 0x0003, T_U16}, {C_WIN, 0x0004, T_U16}, {C_WIN, 0x0005, T_U16}, {C_WIN, 0x0006, T_U16},
    {C_WIN, 0x0007, T_M8}, {C_WIN, 0x0008, T_U8}, {C_WIN, 0x0009, T_U8}, {C_WIN, 0x000A, T_M8}, {C_WIN, 0x000B, T_U8}, {C_WIN, 0x000C, T_U8}, {C_WIN, 0x000D, T_U8}, {C_WIN, 0x000E, T_U8},
    {C_WIN, 0x000F, T_U8}, {C_WIN, 0x0010, T_U16}, {C_WIN, 0x0011, T_U16}, {C_WIN, 0x0012, T_U16}, {C_WIN, 0x0013, T_U16}, {C_WIN, 0x0014, T_U16}, {C_WIN, 0x0015, T_U16},
    {C_WIN, 0x0016, T_U16}, {C_WIN, 0x0017, T_M8}, {C_WIN, 0x0018, T_U8}, {C_WIN, 0x0019, T_U8},
    {C_THERM, 0x0000, T_S16}, {C_THERM, 0x0001, T_S16}, {C_THERM, 0x0002, T_M8}, {C_THERM, 0x0003, T_S16}, {C_THERM, 0x0004, T_S16}, {C_THERM, 0x0005, T_S16}, {C_THERM, 0x0006, T_S16},
    {C_THERM, 0x0011, T_S16}, {C_THERM, 0x0012, T_S16}, {C_THERM, 0x001B, T_E8}, {C_THERM, 0x001C, T_E8},
    {C_FAN, 0x0000, T_E8}, {C_FAN, 0x0001, T_E8},
    {C_COLOR, 0x0000, T_U8}, {C_COLOR, 0x0001, T_U8}, {C_COLOR, 0x0002, T_U16}, {C_COLOR, 0x0003, T_U16}, {C_COLOR, 0x0004, T_U16}, {C_COLOR, 0x0007, T_U16}, {C_COLOR, 0x0008, T_E8},
    {C_COLOR, 0x000F, T_M8}, {C_COLOR, 0x4001, T_E8}, {C_COLOR, 0x400A, T_M16}, {C_COLOR, 0x400B, T_U16}, {C_COLOR, 0x400C, T_U16},
    {C_LUX, 0x0000, T_U16}, {C_LUX, 0x0001, T_U16}, {C_LUX, 0x0002, T_U16}, {C_LUX, 0x0003, T_U16}, {C_LUX, 0x0004, T_E8},
    {C_TEMP, 0x0000, T_S16}, {C_TEMP, 0x0001, T_S16}, {C_TEMP, 0x0002, T_S16}, {C_TEMP, 0x0003, T_U16},
    {C_PRESS, 0x0000, T_S16}, {C_PRESS, 0x0001, T_S16}, {C_PRESS, 0x0002, T_S16}, {C_PRESS, 0x0003, T_U16},
    {C_PRESS, 0x0010, T_S16}, {C_PRESS, 0x0011, T_S16}, {C_PRESS, 0x0012, T_S16}, {C_PRESS, 0x0013, T_U16}, {C_PRESS, 0x0014, T_S8},
    {C_FLOW, 0x0000, T_U16}, {C_FLOW, 0x0001, T_U16}, {C_FLOW, 0x0002, T_U16}, {C_FLOW, 0x0003, T_U16},
    {C_HUM, 0x0000, T_U16}, {C_HUM, 0x0001, T_U16}, {C_HUM, 0x0002, T_U16}, {C_HUM, 0x0003, T_U16},
    {C_OCC, 0x0000, T_M8}, {C_OCC, 0x0001, T_E8}, {C_OCC, 0x0002, T_M8}, {C_OCC, 0x0010, T_U16}, {C_OCC, 0x0011, T_U16}, {C_OCC, 0x0012, T_U8},
    {C_IAS, 0x0000, T_E8}, {C_IAS, 0x0001, ESP_ZB_ZCL_ATTR_TYPE_16BIT_ENUM}, {C_IAS, 0x0002, T_M16}, {C_IAS, 0x0010, T_IEEE}, {C_IAS, 0x0011, T_U8},
    {C_CO2, 0x0000, T_F}, {C_CO2, 0x0001, T_F}, {C_CO2, 0x0002, T_F}, {C_CO2, 0x0003, T_F},
    {C_PM25, 0x0000, T_F}, {C_PM25, 0x0001, T_F}, {C_PM25, 0x0002, T_F}, {C_PM25, 0x0003, T_F},
    {C_WIND, 0x0000, T_U16}, {C_WIND, 0x0001, T_U16}, {C_WIND, 0x0002, T_U16}, {C_WIND, 0x0003, T_U16},
    {C_ELEC, 0x0000, ESP_ZB_ZCL_ATTR_TYPE_32BITMAP},
};

uint8_t tipoDoAtributo(uint16_t cluster, uint16_t atributo)
{
    for (const TipoDoAtributo &t : TIPOS)
        if (t.cluster == cluster && t.atributo == atributo) return t.tipo;
    if (cluster == C_ELEC) {
        /* A medicao eletrica: numeros com sinal para potencia e o que e DC. */
        if (atributo >= 0x0100 && atributo <= 0x0108) return T_S16; /* DC tensao, corrente, potencia */
        if (atributo >= 0x0200 && atributo <= 0x0207) return T_U16; /* os fatores do DC */
        if (atributo == 0x0510) return T_S8;                        /* fator de potencia */
        if (atributo == 0x050B || atributo == 0x050D || atributo == 0x050F) return T_S16; /* potencia ativa */
        if (atributo == 0x0910 || atributo == 0x0A10) return T_S8;
        if (atributo == 0x090B || atributo == 0x0A0B || atributo == 0x090D || atributo == 0x0A0D) return T_S16;
        return T_U16;
    }
    if (cluster == C_OTA) {
        if (atributo == 0x0000) return T_IEEE;
        if (atributo == 0x0001 || atributo == 0x0002) return T_U32;
        if (atributo == 0x0006) return T_E8;
        if (atributo == 0x0007 || atributo == 0x0008 || atributo == 0x0009) return T_U16;
        return T_U16;
    }
    return T_U16;
}

uint16_t tamanhoDoValor(uint8_t tipo, const void *valor)
{
    const uint8_t *b = (const uint8_t *)valor;
    switch (tipo) {
    case ESP_ZB_ZCL_ATTR_TYPE_CHAR_STRING:
    case ESP_ZB_ZCL_ATTR_TYPE_OCTET_STRING: return b ? (uint16_t)(1 + (b[0] == 0xFF ? 0 : b[0])) : 1;
    case ESP_ZB_ZCL_ATTR_TYPE_LONG_CHAR_STRING:
    case ESP_ZB_ZCL_ATTR_TYPE_LONG_OCTET_STRING:
    case ESP_ZB_ZCL_ATTR_TYPE_ARRAY:
    case ESP_ZB_ZCL_ATTR_TYPE_16BIT_ARRAY:
    case ESP_ZB_ZCL_ATTR_TYPE_32BIT_ARRAY: {
        if (!b) return 2;
        uint16_t n = (uint16_t)(b[0] | (b[1] << 8));
        return (uint16_t)(2 + (n == 0xFFFF ? 0 : n));
    }
    case ESP_ZB_ZCL_ATTR_TYPE_BOOL:
    case ESP_ZB_ZCL_ATTR_TYPE_8BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_8BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U8:
    case ESP_ZB_ZCL_ATTR_TYPE_S8:
    case ESP_ZB_ZCL_ATTR_TYPE_8BIT_ENUM: return 1;
    case ESP_ZB_ZCL_ATTR_TYPE_16BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_16BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U16:
    case ESP_ZB_ZCL_ATTR_TYPE_S16:
    case ESP_ZB_ZCL_ATTR_TYPE_16BIT_ENUM:
    case ESP_ZB_ZCL_ATTR_TYPE_SEMI:
    case ESP_ZB_ZCL_ATTR_TYPE_CLUSTER_ID:
    case ESP_ZB_ZCL_ATTR_TYPE_ATTRIBUTE_ID: return 2;
    case ESP_ZB_ZCL_ATTR_TYPE_24BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_24BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U24:
    case ESP_ZB_ZCL_ATTR_TYPE_S24: return 3;
    case ESP_ZB_ZCL_ATTR_TYPE_32BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_32BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U32:
    case ESP_ZB_ZCL_ATTR_TYPE_S32:
    case ESP_ZB_ZCL_ATTR_TYPE_SINGLE:
    case ESP_ZB_ZCL_ATTR_TYPE_TIME_OF_DAY:
    case ESP_ZB_ZCL_ATTR_TYPE_DATE:
    case ESP_ZB_ZCL_ATTR_TYPE_UTC_TIME:
    case ESP_ZB_ZCL_ATTR_TYPE_BACNET_OID: return 4;
    case ESP_ZB_ZCL_ATTR_TYPE_40BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_40BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U40:
    case ESP_ZB_ZCL_ATTR_TYPE_S40: return 5;
    case ESP_ZB_ZCL_ATTR_TYPE_48BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_48BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U48:
    case ESP_ZB_ZCL_ATTR_TYPE_S48: return 6;
    case ESP_ZB_ZCL_ATTR_TYPE_56BIT:
    case ESP_ZB_ZCL_ATTR_TYPE_56BITMAP:
    case ESP_ZB_ZCL_ATTR_TYPE_U56:
    case ESP_ZB_ZCL_ATTR_TYPE_S56: return 7;
    case ESP_ZB_ZCL_ATTR_TYPE_128_BIT_KEY: return 16;
    default: return 8;
    }
}

/* O valor em numero, para comparar com o delta dos relatorios. */
double numeroDoValor(uint8_t tipo, const void *valor)
{
    const uint8_t *b = (const uint8_t *)valor;
    switch (tipo) {
    case ESP_ZB_ZCL_ATTR_TYPE_S8: return *(const int8_t *)b;
    case ESP_ZB_ZCL_ATTR_TYPE_S16: return (int16_t)(b[0] | (b[1] << 8));
    case ESP_ZB_ZCL_ATTR_TYPE_S32: return (int32_t)(b[0] | (b[1] << 8) | (b[2] << 16) | ((uint32_t)b[3] << 24));
    case ESP_ZB_ZCL_ATTR_TYPE_SINGLE: {
        float f;
        memcpy(&f, b, 4);
        return f;
    }
    default: {
        uint16_t n = tamanhoDoValor(tipo, b);
        if (n > 8) return 0;
        uint64_t v = 0;
        memcpy(&v, b, n);
        return (double)v;
    }
    }
}

/* =====================================================================
 * o estado da placa */
enum TipoDeQuadro : uint8_t {
    Q_BEACON_REQ = 1,
    Q_BEACON,
    Q_JOIN_REQ,
    Q_JOIN_RESP,
    Q_ANNCE,
    Q_MATCH_REQ,
    Q_MATCH_RESP,
    Q_BIND_REQ,
    Q_BIND_RESP,
    Q_ZCL,
    Q_LEAVE,
};

/* Os comandos gerais do ZCL (a tabela 2-3 da especificacao). */
enum { ZCL_LER = 0x00, ZCL_LER_RESPOSTA = 0x01, ZCL_ESCREVER = 0x02, ZCL_ESCREVER_RESPOSTA = 0x04, ZCL_CONFIGURAR = 0x06, ZCL_CONFIGURAR_RESPOSTA = 0x07, ZCL_RELATORIO = 0x0A, ZCL_RESPOSTA_PADRAO = 0x0B };

const uint8_t CANAL_DE_TODOS = 200; /* o pedido de beacon vai para todos os canais */
const int INICIO_DOS_CANAIS = 100;  /* separa do OpenThread, que usa os numeros 11 a 26 */

struct Sinal {
    uint32_t tipo;
    esp_err_t erro;
    std::vector<uint8_t> parametros;
};
struct Alarme {
    esp_zb_callback_t funcao;
    uint8_t parametro;
    uint32_t quando;
};
struct Vizinho {
    uint16_t curto;
    esp_zb_ieee_addr_t longo;
};
struct Ligacao { /* uma linha da tabela de binding */
    uint8_t epOrigem;
    uint16_t cluster;
    uint8_t modo;
    esp_zb_addr_u destino;
    uint8_t epDestino;
};
struct Relatorio {
    esp_zb_zcl_reporting_info_t info;
    double ultimo;
    bool enviado;
    uint32_t quando;
    uint16_t minimo, maximo;
};
struct PedidoDeMatch {
    esp_zb_zdo_match_desc_callback_t funcao;
    void *contexto;
    uint32_t prazo;
    bool difusao, respondido;
    uint8_t tsn;
};
struct PedidoDeBind {
    esp_zb_zdo_bind_callback_t funcao;
    void *contexto;
    uint32_t prazo;
    uint8_t tsn;
};

esp_zb_ep_list_t *dispositivo = nullptr;
esp_zb_core_action_callback_t acao = nullptr;
esp_zb_apsde_data_indication_callback_t indicacaoAps = nullptr;
esp_zb_nwk_device_type_t papel = ESP_ZB_DEVICE_TYPE_ED;
SemaphoreHandle_t travaDoZigbee = nullptr;

uint32_t mascaraPrimaria = ESP_ZB_TRANSCEIVER_ALL_CHANNELS_MASK, mascaraGeral = ESP_ZB_TRANSCEIVER_ALL_CHANNELS_MASK;
bool naRede = false, novaDeFabrica = true, procurando = false, ligado = false;
uint16_t minhaPan = 0xFFFF, meuCurto = 0xFFFF;
esp_zb_ieee_addr_t meuLongo, minhaPanLonga;
uint8_t meuCanal = 0, tsn = 0;
uint32_t aberturaAte = 0, proximoBeacon = 0, fimDaProcura = 0;
uint8_t duracaoDaVarredura = 3;

std::vector<Sinal> sinais;
std::vector<Alarme> alarmes;
std::vector<Vizinho> vizinhos;
std::vector<Ligacao> ligacoes;
std::vector<Relatorio> relatorios;
std::vector<PedidoDeMatch> matches;
std::vector<PedidoDeBind> binds;
std::vector<esp_zb_network_descriptor_t> redesAchadas;
esp_zb_zdo_scan_complete_callback_t fimDaVarredura = nullptr;
uint32_t prazoDaVarredura = 0;

void travar()
{
    if (!travaDoZigbee) travaDoZigbee = xSemaphoreCreateRecursiveMutex();
    xSemaphoreTakeRecursive(travaDoZigbee, portMAX_DELAY);
}
void destravar() { xSemaphoreGiveRecursive(travaDoZigbee); }

void meuEndereco()
{
    uint64_t mac = ESP.getEfuseMac();
    /* O EUI-64 do MAC, como o do chip (a ordem e a do ar: menos significativo primeiro). */
    meuLongo[7] = (uint8_t)mac;
    meuLongo[6] = (uint8_t)(mac >> 8);
    meuLongo[5] = (uint8_t)(mac >> 16);
    meuLongo[4] = 0xFF;
    meuLongo[3] = 0xFE;
    meuLongo[2] = (uint8_t)(mac >> 24);
    meuLongo[1] = (uint8_t)(mac >> 32);
    meuLongo[0] = (uint8_t)(mac >> 40);
}

void anotarVizinho(uint16_t curto, const uint8_t *longo)
{
    for (Vizinho &v : vizinhos)
        if (v.curto == curto || !memcmp(v.longo, longo, 8)) {
            v.curto = curto;
            memcpy(v.longo, longo, 8);
            return;
        }
    Vizinho v;
    v.curto = curto;
    memcpy(v.longo, longo, 8);
    vizinhos.push_back(v);
}

uint16_t curtoDoLongo(const uint8_t *longo)
{
    for (const Vizinho &v : vizinhos)
        if (!memcmp(v.longo, longo, 8)) return v.curto;
    return 0xFFFF;
}

void novoSinal(uint32_t tipo, esp_err_t erro, const void *parametros = nullptr, size_t n = 0)
{
    Sinal s;
    s.tipo = tipo;
    s.erro = erro;
    if (n) s.parametros.assign((const uint8_t *)parametros, (const uint8_t *)parametros + n);
    sinais.push_back(s);
}

/* =====================================================================
 * os quadros: [Z B tipo pan(2) origem(2) destino(2) epOrigem epDestino cluster(2) tsn] + dados */
struct Quadro {
    std::vector<uint8_t> b;
    Quadro(uint8_t tipo, uint16_t destino, uint8_t epOrigem = 0, uint8_t epDestino = 0, uint16_t cluster = 0, uint8_t numero = 0)
    {
        b = {'Z', 'B', tipo};
        u16(minhaPan);
        u16(meuCurto);
        u16(destino);
        u8(epOrigem);
        u8(epDestino);
        u16(cluster);
        u8(numero);
    }
    void u8(uint8_t v) { b.push_back(v); }
    void u16(uint16_t v)
    {
        b.push_back((uint8_t)v);
        b.push_back((uint8_t)(v >> 8));
    }
    void dados(const void *p, size_t n) { b.insert(b.end(), (const uint8_t *)p, (const uint8_t *)p + n); }
    void enviar(uint8_t canal) const { cs_obj_n(nullptr, "__154_enviar", (int)canal, cs_vetor(b.data(), (int)b.size())); }
};

struct Leitor {
    const uint8_t *p;
    size_t n;
    uint8_t u8() { return n ? (n--, *p++) : 0; }
    uint16_t u16()
    {
        uint16_t a = u8();
        return (uint16_t)(a | (u8() << 8));
    }
    const uint8_t *pular(size_t k)
    {
        const uint8_t *q = p;
        if (k > n) k = n;
        p += k;
        n -= k;
        return q;
    }
};

uint8_t canalDoAr() { return (uint8_t)(INICIO_DOS_CANAIS + meuCanal); }

/* =====================================================================
 * o modelo de dados */
esp_zb_endpoint_t *acharEndpoint(uint8_t ep)
{
    for (esp_zb_ep_list_t *e = dispositivo; e; e = e->next)
        if (e->endpoint.ep_id == ep) return &e->endpoint;
    return nullptr;
}

esp_zb_attribute_list_t *acharCluster(uint8_t ep, uint16_t cluster, uint8_t papelDoCluster)
{
    esp_zb_endpoint_t *e = acharEndpoint(ep);
    if (!e) return nullptr;
    for (esp_zb_cluster_list_t *c = e->cluster_list; c; c = c->next)
        if (c->cluster.cluster_id == cluster && c->cluster.attr_list && (!papelDoCluster || (c->cluster.role_mask & papelDoCluster))) return c->cluster.attr_list;
    return nullptr;
}

esp_zb_zcl_attr_t *acharAtributo(esp_zb_attribute_list_t *lista, uint16_t atributo)
{
    for (esp_zb_attribute_list_t *a = lista; a; a = a->next)
        if (a->attribute.id == atributo && a->attribute.data_p) return &a->attribute;
    return nullptr;
}

esp_zb_attribute_list_t *novaListaDeAtributos(uint16_t cluster)
{
    auto *l = (esp_zb_attribute_list_t *)calloc(1, sizeof(esp_zb_attribute_list_t));
    l->attribute.id = 0xFFFF;
    l->cluster_id = cluster;
    return l;
}

void *copiaDoValor(uint8_t tipo, const void *valor)
{
    uint16_t n = tamanhoDoValor(tipo, valor);
    /* Espaco de sobra: a string pode crescer ate o limite quando o sketch a troca. */
    void *d = calloc(1, n < 256 && (tipo == ESP_ZB_ZCL_ATTR_TYPE_CHAR_STRING || tipo == ESP_ZB_ZCL_ATTR_TYPE_OCTET_STRING) ? 256 : n);
    if (valor) memcpy(d, valor, n);
    return d;
}

esp_err_t acrescentar(esp_zb_attribute_list_t *lista, uint16_t cluster, uint16_t atributo, uint8_t tipo, uint8_t acesso, const void *valor)
{
    if (!lista) return ESP_ERR_INVALID_ARG;
    esp_zb_attribute_list_t *fim = lista;
    for (esp_zb_attribute_list_t *a = lista; a; a = a->next) {
        if (a->attribute.id == atributo && a != lista) return ESP_ERR_INVALID_ARG;
        fim = a;
    }
    auto *novo = (esp_zb_attribute_list_t *)calloc(1, sizeof(esp_zb_attribute_list_t));
    novo->attribute.id = atributo;
    novo->attribute.type = tipo;
    novo->attribute.access = acesso;
    novo->attribute.manuf_code = ESP_ZB_ZCL_ATTR_NON_MANUFACTURER_SPECIFIC;
    novo->attribute.data_p = copiaDoValor(tipo, valor);
    novo->cluster_id = cluster;
    fim->next = novo;
    return ESP_OK;
}

esp_err_t acrescentarDaTabela(esp_zb_attribute_list_t *lista, uint16_t atributo, const void *valor)
{
    if (!lista) return ESP_ERR_INVALID_ARG;
    return acrescentar(lista, lista->cluster_id, atributo, tipoDoAtributo(lista->cluster_id, atributo),
                       ESP_ZB_ZCL_ATTR_ACCESS_READ_WRITE | ESP_ZB_ZCL_ATTR_ACCESS_REPORTING, valor);
}

esp_err_t acrescentarCluster(esp_zb_cluster_list_t *lista, esp_zb_attribute_list_t *atributos, uint16_t cluster, uint8_t papelDoCluster)
{
    if (!lista) return ESP_ERR_INVALID_ARG;
    if (!atributos) atributos = novaListaDeAtributos(cluster);
    esp_zb_cluster_list_t *fim = lista;
    while (fim->next) fim = fim->next;
    auto *novo = (esp_zb_cluster_list_t *)calloc(1, sizeof(esp_zb_cluster_list_t));
    novo->cluster.cluster_id = cluster;
    novo->cluster.attr_list = atributos;
    novo->cluster.role_mask = papelDoCluster;
    novo->cluster.manuf_code = ESP_ZB_ZCL_ATTR_NON_MANUFACTURER_SPECIFIC;
    fim->next = novo;
    lista->cluster.attr_count++;
    return ESP_OK;
}

/* =====================================================================
 * o envio pelo binding ou direto */
void comoEnviar(const esp_zb_zcl_basic_cmd_t &basico, esp_zb_zcl_address_mode_t modo, uint16_t cluster, std::vector<std::pair<uint16_t, uint8_t>> &destinos)
{
    switch (modo) {
    case ESP_ZB_APS_ADDR_MODE_16_ENDP_PRESENT: destinos.push_back({basico.dst_addr_u.addr_short, basico.dst_endpoint}); break;
    case ESP_ZB_APS_ADDR_MODE_64_ENDP_PRESENT: {
        uint16_t curto = curtoDoLongo(basico.dst_addr_u.addr_long);
        if (curto != 0xFFFF) destinos.push_back({curto, basico.dst_endpoint});
        break;
    }
    case ESP_ZB_APS_ADDR_MODE_16_GROUP_ENDP_NOT_PRESENT:
        /* O grupo vai para todos: o simulador nao guarda as tabelas de grupo. */
        destinos.push_back({0xFFFF, 0xFF});
        break;
    default:
        for (const Ligacao &l : ligacoes) {
            if (l.epOrigem != basico.src_endpoint || l.cluster != cluster) continue;
            uint16_t curto = l.modo == ESP_ZB_ZDO_BIND_DST_ADDR_MODE_16_BIT_GROUP ? 0xFFFF : curtoDoLongo(l.destino.addr_long);
            if (curto != 0xFFFF) destinos.push_back({curto, l.epDestino});
        }
        break;
    }
}

/* Comando de cluster (ou global, com global = true): [global cmd direcao] + dados */
uint8_t enviarZcl(const esp_zb_zcl_basic_cmd_t &basico, esp_zb_zcl_address_mode_t modo, uint16_t cluster, bool global, uint8_t comando, uint8_t direcao,
                  const void *dados, size_t n, uint8_t numero = 0xFF)
{
    if (numero == 0xFF) numero = ++tsn;
    std::vector<std::pair<uint16_t, uint8_t>> destinos;
    comoEnviar(basico, modo, cluster, destinos);
    for (auto &d : destinos) {
        Quadro q(Q_ZCL, d.first, basico.src_endpoint, d.second, cluster, numero);
        q.u8(global);
        q.u8(comando);
        q.u8(direcao);
        q.dados(dados, n);
        q.enviar(canalDoAr());
    }
    return numero;
}

void responderZcl(uint16_t para, uint8_t epOrigem, uint8_t epDestino, uint16_t cluster, bool global, uint8_t comando, uint8_t direcao, const void *dados, size_t n, uint8_t numero)
{
    esp_zb_zcl_basic_cmd_t basico = {};
    basico.dst_addr_u.addr_short = para;
    basico.dst_endpoint = epDestino;
    basico.src_endpoint = epOrigem;
    enviarZcl(basico, ESP_ZB_APS_ADDR_MODE_16_ENDP_PRESENT, cluster, global, comando, direcao, dados, n, numero);
}

/* O ZCL_SEND de atributo: [id tipo tamanho(2) valor] */
void poeAtributo(std::vector<uint8_t> &saida, uint16_t id, uint8_t tipo, const void *valor)
{
    uint16_t n = tamanhoDoValor(tipo, valor);
    saida.push_back((uint8_t)id);
    saida.push_back((uint8_t)(id >> 8));
    saida.push_back(tipo);
    saida.push_back((uint8_t)n);
    saida.push_back((uint8_t)(n >> 8));
    saida.insert(saida.end(), (const uint8_t *)valor, (const uint8_t *)valor + n);
}

esp_zb_zcl_cmd_info_t infoDoComando(uint16_t origem, uint8_t epOrigem, uint8_t epDestino, uint16_t cluster, uint8_t numero)
{
    esp_zb_zcl_cmd_info_t info = {};
    info.status = ESP_ZB_ZCL_STATUS_SUCCESS;
    info.header.tsn = numero;
    info.src_address.addr_type = ESP_ZB_ZCL_ADDR_TYPE_SHORT;
    info.src_address.u.short_addr = origem;
    info.dst_address = meuCurto;
    info.src_endpoint = epOrigem;
    info.dst_endpoint = epDestino;
    info.cluster = cluster;
    info.profile = ESP_ZB_AF_HA_PROFILE_ID;
    return info;
}

void avisarAtributo(uint8_t ep, uint16_t cluster, uint16_t id, uint8_t tipo, void *valor)
{
    if (!acao) return;
    esp_zb_zcl_set_attr_value_message_t m = {};
    m.info.status = ESP_ZB_ZCL_STATUS_SUCCESS;
    m.info.dst_endpoint = ep;
    m.info.cluster = cluster;
    m.attribute.id = id;
    m.attribute.data.type = (esp_zb_zcl_attr_type_t)tipo;
    m.attribute.data.size = tamanhoDoValor(tipo, valor);
    m.attribute.data.value = valor;
    acao(ESP_ZB_CORE_SET_ATTR_VALUE_CB_ID, &m);
}

/* Muda o atributo do lado servidor do cluster; os relatorios olham depois. */
void mudarEAvisar(uint8_t ep, uint16_t cluster, uint16_t id, const void *valor)
{
    esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(ep, cluster, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), id);
    if (!a) return;
    memcpy(a->data_p, valor, tamanhoDoValor(a->type, valor));
    avisarAtributo(ep, cluster, id, a->type, a->data_p);
}

/* =====================================================================
 * o que chega do ar */
void tratarGlobal(uint16_t origem, uint8_t epOrigem, uint8_t epDestino, uint16_t cluster, uint8_t comando, uint8_t direcao, uint8_t numero, Leitor d)
{
    /* O lado do cluster que responde: o pedido para o servidor procura o servidor. */
    uint8_t lado = direcao == ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI ? ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE : ESP_ZB_ZCL_CLUSTER_SERVER_ROLE;
    switch (comando) {
    case ZCL_LER: {
        std::vector<uint8_t> saida;
        esp_zb_attribute_list_t *lista = acharCluster(epDestino, cluster, lado);
        if (!lista) lista = acharCluster(epDestino, cluster, 0);
        while (d.n >= 2) {
            uint16_t id = d.u16();
            esp_zb_zcl_attr_t *a = acharAtributo(lista, id);
            saida.push_back(a ? ESP_ZB_ZCL_STATUS_SUCCESS : ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB);
            if (a) poeAtributo(saida, id, a->type, a->data_p);
            else {
                saida.push_back((uint8_t)id);
                saida.push_back((uint8_t)(id >> 8));
            }
        }
        responderZcl(origem, epDestino, epOrigem, cluster, true, ZCL_LER_RESPOSTA, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, saida.data(), saida.size(), numero);
        break;
    }
    case ZCL_LER_RESPOSTA: {
        if (!acao) break;
        esp_zb_zcl_cmd_read_attr_resp_message_t m = {};
        m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
        std::vector<esp_zb_zcl_read_attr_resp_variable_t> vars;
        vars.reserve(32);
        while (d.n && vars.size() < 32) {
            esp_zb_zcl_read_attr_resp_variable_t v = {};
            v.status = (esp_zb_zcl_status_t)d.u8();
            v.attribute.id = d.u16();
            if (v.status == ESP_ZB_ZCL_STATUS_SUCCESS) {
                v.attribute.data.type = (esp_zb_zcl_attr_type_t)d.u8();
                v.attribute.data.size = d.u16();
                v.attribute.data.value = (void *)d.pular(v.attribute.data.size);
            }
            vars.push_back(v);
        }
        for (size_t i = 0; i + 1 < vars.size(); i++) vars[i].next = &vars[i + 1];
        m.variables = vars.empty() ? nullptr : &vars[0];
        acao(ESP_ZB_CORE_CMD_READ_ATTR_RESP_CB_ID, &m);
        break;
    }
    case ZCL_ESCREVER: {
        std::vector<uint8_t> saida;
        esp_zb_attribute_list_t *lista = acharCluster(epDestino, cluster, lado);
        if (!lista) lista = acharCluster(epDestino, cluster, 0);
        while (d.n >= 5) {
            uint16_t id = d.u16();
            uint8_t tipo = d.u8();
            uint16_t n = d.u16();
            const uint8_t *valor = d.pular(n);
            esp_zb_zcl_attr_t *a = acharAtributo(lista, id);
            uint8_t estado = ESP_ZB_ZCL_STATUS_SUCCESS;
            if (!a) estado = ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB;
            else if (a->type != tipo) estado = ESP_ZB_ZCL_STATUS_INVALID_TYPE;
            else if (!(a->access & ESP_ZB_ZCL_ATTR_ACCESS_WRITE_ONLY)) estado = ESP_ZB_ZCL_STATUS_READ_ONLY;
            else {
                memcpy(a->data_p, valor, n);
                avisarAtributo(epDestino, cluster, id, a->type, a->data_p);
            }
            saida.push_back(estado);
            saida.push_back((uint8_t)id);
            saida.push_back((uint8_t)(id >> 8));
        }
        responderZcl(origem, epDestino, epOrigem, cluster, true, ZCL_ESCREVER_RESPOSTA, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, saida.data(), saida.size(), numero);
        break;
    }
    case ZCL_ESCREVER_RESPOSTA: {
        if (!acao) break;
        esp_zb_zcl_cmd_write_attr_resp_message_t m = {};
        m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
        std::vector<esp_zb_zcl_write_attr_resp_variable_t> vars;
        vars.reserve(32);
        while (d.n >= 3 && vars.size() < 32) {
            esp_zb_zcl_write_attr_resp_variable_t v = {};
            v.status = (esp_zb_zcl_status_t)d.u8();
            v.attribute_id = d.u16();
            vars.push_back(v);
        }
        for (size_t i = 0; i + 1 < vars.size(); i++) vars[i].next = &vars[i + 1];
        m.variables = vars.empty() ? nullptr : &vars[0];
        acao(ESP_ZB_CORE_CMD_WRITE_ATTR_RESP_CB_ID, &m);
        break;
    }
    case ZCL_CONFIGURAR: {
        std::vector<uint8_t> saida;
        while (d.n >= 7) {
            uint16_t id = d.u16();
            uint8_t tipo = d.u8();
            uint16_t minimo = d.u16(), maximo = d.u16();
            double delta = 0;
            memcpy(&delta, d.pular(8), 8);
            esp_zb_attribute_list_t *lista = acharCluster(epDestino, cluster, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
            uint8_t estado = acharAtributo(lista, id) ? ESP_ZB_ZCL_STATUS_SUCCESS : ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB;
            if (!estado) {
                Relatorio *r = nullptr;
                for (Relatorio &x : relatorios)
                    if (x.info.ep == epDestino && x.info.cluster_id == cluster && x.info.attr_id == id) r = &x;
                if (!r) {
                    relatorios.push_back({});
                    r = &relatorios.back();
                    r->info.ep = epDestino;
                    r->info.cluster_id = cluster;
                    r->info.attr_id = id;
                }
                r->minimo = minimo;
                r->maximo = maximo;
                memcpy(&r->info.u.send_info.delta, &delta, sizeof(delta));
                r->enviado = false;
            }
            (void)tipo;
            saida.push_back(estado);
            saida.push_back(ESP_ZB_ZCL_REPORT_DIRECTION_SEND);
            saida.push_back((uint8_t)id);
            saida.push_back((uint8_t)(id >> 8));
        }
        responderZcl(origem, epDestino, epOrigem, cluster, true, ZCL_CONFIGURAR_RESPOSTA, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, saida.data(), saida.size(), numero);
        break;
    }
    case ZCL_CONFIGURAR_RESPOSTA: {
        if (!acao) break;
        esp_zb_zcl_cmd_config_report_resp_message_t m = {};
        m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
        std::vector<esp_zb_zcl_config_report_resp_variable_t> vars;
        vars.reserve(32);
        while (d.n >= 4 && vars.size() < 32) {
            esp_zb_zcl_config_report_resp_variable_t v = {};
            v.status = (esp_zb_zcl_status_t)d.u8();
            v.direction = d.u8();
            v.attribute_id = d.u16();
            vars.push_back(v);
        }
        for (size_t i = 0; i + 1 < vars.size(); i++) vars[i].next = &vars[i + 1];
        m.variables = vars.empty() ? nullptr : &vars[0];
        acao(ESP_ZB_CORE_CMD_REPORT_CONFIG_RESP_CB_ID, &m);
        break;
    }
    case ZCL_RELATORIO: {
        if (!acao) break;
        while (d.n >= 5) {
            esp_zb_zcl_report_attr_message_t m = {};
            m.status = ESP_ZB_ZCL_STATUS_SUCCESS;
            m.src_address.addr_type = ESP_ZB_ZCL_ADDR_TYPE_SHORT;
            m.src_address.u.short_addr = origem;
            m.src_endpoint = epOrigem;
            m.dst_endpoint = epDestino;
            m.cluster = cluster;
            m.attribute.id = d.u16();
            m.attribute.data.type = (esp_zb_zcl_attr_type_t)d.u8();
            m.attribute.data.size = d.u16();
            m.attribute.data.value = (void *)d.pular(m.attribute.data.size);
            acao(ESP_ZB_CORE_REPORT_ATTR_CB_ID, &m);
        }
        break;
    }
    case ZCL_RESPOSTA_PADRAO: {
        if (!acao) break;
        esp_zb_zcl_cmd_default_resp_message_t m = {};
        m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
        m.resp_to_cmd = d.u8();
        m.status_code = (esp_zb_zcl_status_t)d.u8();
        acao(ESP_ZB_CORE_CMD_DEFAULT_RESP_CB_ID, &m);
        break;
    }
    default: break;
    }
}

void respostaPadrao(uint16_t origem, uint8_t epOrigem, uint8_t epDestino, uint16_t cluster, uint8_t comando, uint8_t numero, uint8_t estado)
{
    uint8_t r[2] = {comando, estado};
    responderZcl(origem, epDestino, epOrigem, cluster, true, ZCL_RESPOSTA_PADRAO, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, r, 2, numero);
}

bool lerBooleano(uint8_t ep, uint16_t cluster, uint16_t id)
{
    esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(ep, cluster, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), id);
    return a && *(uint8_t *)a->data_p;
}

void tratarOnOff(uint8_t ep, uint8_t comando)
{
    bool valor;
    switch (comando) {
    case ESP_ZB_ZCL_CMD_ON_OFF_OFF_ID:
    case ESP_ZB_ZCL_CMD_ON_OFF_OFF_WITH_EFFECT_ID: valor = false; break;
    case ESP_ZB_ZCL_CMD_ON_OFF_ON_ID:
    case ESP_ZB_ZCL_CMD_ON_OFF_ON_WITH_RECALL_GLOBAL_SCENE_ID:
    case ESP_ZB_ZCL_CMD_ON_OFF_ON_WITH_TIMED_OFF_ID: valor = true; break;
    case ESP_ZB_ZCL_CMD_ON_OFF_TOGGLE_ID: valor = !lerBooleano(ep, C_ONOFF, ESP_ZB_ZCL_ATTR_ON_OFF_ON_OFF_ID); break;
    default: return;
    }
    uint8_t v = valor;
    mudarEAvisar(ep, C_ONOFF, ESP_ZB_ZCL_ATTR_ON_OFF_ON_OFF_ID, &v);
}

void tratarComando(uint16_t origem, uint8_t epOrigem, uint8_t epDestino, uint16_t cluster, uint8_t comando, uint8_t numero, Leitor d)
{
    uint8_t estado = ESP_ZB_ZCL_STATUS_SUCCESS;
    if (cluster == C_ONOFF) tratarOnOff(epDestino, comando);
    else if (cluster == C_LEVEL) {
        esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(epDestino, C_LEVEL, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), ESP_ZB_ZCL_ATTR_LEVEL_CONTROL_CURRENT_LEVEL_ID);
        uint8_t nivel = a ? *(uint8_t *)a->data_p : 0;
        bool comOnOff = false;
        switch (comando) {
        case ESP_ZB_ZCL_CMD_LEVEL_CONTROL_MOVE_TO_LEVEL_WITH_ON_OFF: comOnOff = true; [[fallthrough]];
        case ESP_ZB_ZCL_CMD_LEVEL_CONTROL_MOVE_TO_LEVEL: nivel = d.u8(); break;
        case ESP_ZB_ZCL_CMD_LEVEL_CONTROL_STEP_WITH_ON_OFF: comOnOff = true; [[fallthrough]];
        case ESP_ZB_ZCL_CMD_LEVEL_CONTROL_STEP: {
            uint8_t modo = d.u8(), passo = d.u8();
            int novo = modo == 0 ? nivel + passo : nivel - passo;
            nivel = (uint8_t)(novo < 0 ? 0 : novo > 254 ? 254 : novo);
            break;
        }
        default: estado = ESP_ZB_ZCL_STATUS_UNSUP_CLUST_CMD; break;
        }
        if (!estado) {
            if (comOnOff) {
                uint8_t v = nivel > 0;
                mudarEAvisar(epDestino, C_ONOFF, ESP_ZB_ZCL_ATTR_ON_OFF_ON_OFF_ID, &v);
            }
            mudarEAvisar(epDestino, C_LEVEL, ESP_ZB_ZCL_ATTR_LEVEL_CONTROL_CURRENT_LEVEL_ID, &nivel);
        }
    } else if (cluster == C_COLOR) {
        if (comando == ESP_ZB_ZCL_CMD_COLOR_CONTROL_MOVE_TO_COLOR) {
            uint16_t x = d.u16(), y = d.u16();
            mudarEAvisar(epDestino, C_COLOR, ESP_ZB_ZCL_ATTR_COLOR_CONTROL_CURRENT_X_ID, &x);
            mudarEAvisar(epDestino, C_COLOR, ESP_ZB_ZCL_ATTR_COLOR_CONTROL_CURRENT_Y_ID, &y);
        } else estado = ESP_ZB_ZCL_STATUS_UNSUP_CLUST_CMD;
    } else if (cluster == C_IAS) {
        if (!acao) return;
        if (comando == ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_STATUS_CHANGE_NOT_ID) {
            esp_zb_zcl_ias_zone_status_change_notification_message_t m = {};
            m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
            m.zone_status = d.u16();
            m.extended_status = d.u8();
            m.zone_id = d.u8();
            m.delay = d.u16();
            acao(ESP_ZB_CORE_CMD_IAS_ZONE_ZONE_STATUS_CHANGE_NOT_ID, &m);
        } else if (comando == ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_ENROLL_REQUEST_ID) {
            esp_zb_zcl_ias_zone_enroll_request_message_t m = {};
            m.info = infoDoComando(origem, epOrigem, epDestino, cluster, numero);
            m.zone_type = d.u16();
            m.manufacturer_code = d.u16();
            acao(ESP_ZB_CORE_CMD_IAS_ZONE_ZONE_ENROLL_REQUEST_ID, &m);
        } else if (comando == ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_ENROLL_RESPONSE_ID) {
            esp_zb_zcl_ias_zone_enroll_response_message_t m = {};
            m.info.status = ESP_ZB_ZCL_STATUS_SUCCESS;
            m.info.dst_endpoint = epDestino;
            m.info.cluster = cluster;
            m.response_code = d.u8();
            m.zone_id = d.u8();
            esp_zb_zcl_attr_t *z = acharAtributo(acharCluster(epDestino, C_IAS, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), ESP_ZB_ZCL_ATTR_IAS_ZONE_ZONEID_ID);
            if (z) *(uint8_t *)z->data_p = m.zone_id;
            acao(ESP_ZB_CORE_IAS_ZONE_ENROLL_RESPONSE_VALUE_CB_ID, &m);
        }
        return; /* O IAS nao pede resposta padrao aqui. */
    } else if (cluster == C_IDENT) {
        if (comando == ESP_ZB_ZCL_CMD_IDENTIFY_IDENTIFY_ID) {
            uint16_t tempo = d.u16();
            mudarEAvisar(epDestino, C_IDENT, ESP_ZB_ZCL_ATTR_IDENTIFY_IDENTIFY_TIME_ID, &tempo);
        }
    } else estado = ESP_ZB_ZCL_STATUS_UNSUP_CLUST_CMD;
    respostaPadrao(origem, epOrigem, epDestino, cluster, comando, numero, estado);
}

bool clusterBate(esp_zb_endpoint_t &e, uint16_t cluster, uint8_t lado)
{
    for (esp_zb_cluster_list_t *c = e.cluster_list; c; c = c->next)
        if (c->cluster.attr_list && c->cluster.cluster_id == cluster && (c->cluster.role_mask & lado)) return true;
    return false;
}

void avisarAps(uint16_t origem, uint16_t cluster)
{
    if (!indicacaoAps) return;
    esp_zb_apsde_data_ind_t ind = {};
    ind.status = 0;
    ind.dst_short_addr = meuCurto;
    ind.src_short_addr = origem;
    ind.profile_id = 0;
    ind.cluster_id = cluster;
    ind.lqi = 255;
    indicacaoAps(ind);
}

void tratarQuadro(const uint8_t *dados, int n)
{
    if (n < 14 || dados[0] != 'Z' || dados[1] != 'B') return;
    Leitor d = {dados + 2, (size_t)n - 2};
    uint8_t tipo = d.u8();
    uint16_t pan = d.u16(), origem = d.u16(), destino = d.u16();
    uint8_t epOrigem = d.u8(), epDestino = d.u8();
    uint16_t cluster = d.u16();
    uint8_t numero = d.u8();

    if (tipo == Q_BEACON_REQ) {
        if (naRede && papel != ESP_ZB_DEVICE_TYPE_ED && minhaPan != 0xFFFF) {
            Quadro q(Q_BEACON, 0xFFFF);
            q.u8(meuCanal);
            q.dados(minhaPanLonga, 8);
            q.u8(aberturaAte && (int32_t)(aberturaAte - millis()) > 0);
            q.enviar(CANAL_DE_TODOS);
        }
        return;
    }
    if (tipo == Q_BEACON) {
        uint8_t canal = d.u8();
        const uint8_t *longo = d.pular(8);
        bool aberta = d.u8();
        if (fimDaVarredura) {
            for (const esp_zb_network_descriptor_t &r : redesAchadas)
                if (r.short_pan_id == pan && r.logic_channel == canal) return;
            esp_zb_network_descriptor_t r = {};
            r.short_pan_id = pan;
            r.permit_joining = aberta;
            memcpy(r.extended_pan_id, longo, 8);
            r.logic_channel = canal;
            r.router_capacity = true;
            r.end_device_capacity = true;
            redesAchadas.push_back(r);
        }
        if (procurando && aberta && !naRede && ((mascaraPrimaria | mascaraGeral) & (1u << canal))) {
            meuCanal = canal;
            minhaPan = pan;
            memcpy(minhaPanLonga, longo, 8);
            Quadro q(Q_JOIN_REQ, origem);
            q.dados(meuLongo, 8);
            q.u8(papel);
            q.enviar(canalDoAr());
        }
        return;
    }

    /* O resto so vale dentro da PAN e para mim (ou para todos). */
    if (minhaPan == 0xFFFF || pan != minhaPan) return;
    if (tipo == Q_JOIN_RESP) {
        const uint8_t *longo = d.pular(8);
        if (naRede || memcmp(longo, meuLongo, 8)) return;
        meuCurto = d.u16();
        naRede = true;
        novaDeFabrica = false;
        procurando = false;
        anotarVizinho(origem, d.pular(8));
        novoSinal(ESP_ZB_BDB_SIGNAL_STEERING, ESP_OK);
        /* O device announce, como no fim do join. */
        Quadro q(Q_ANNCE, 0xFFFF);
        q.u16(meuCurto);
        q.dados(meuLongo, 8);
        q.u8(papel == ESP_ZB_DEVICE_TYPE_ROUTER ? 0x8E : 0x80);
        q.enviar(canalDoAr());
        return;
    }
    if (!naRede) return;
    if (destino != meuCurto && destino != 0xFFFF && destino != 0xFFFD && destino != 0xFFFC) return;
    if (origem == meuCurto) return;

    switch (tipo) {
    case Q_JOIN_REQ: {
        if (papel == ESP_ZB_DEVICE_TYPE_ED) return;
        if (!aberturaAte || (int32_t)(aberturaAte - millis()) <= 0) return;
        const uint8_t *longo = d.pular(8);
        uint16_t curto = curtoDoLongo(longo);
        while (curto == 0xFFFF || curto == 0x0000 || curto >= 0xFFF8) {
            curto = (uint16_t)esp_random();
            for (const Vizinho &v : vizinhos)
                if (v.curto == curto) curto = 0xFFFF;
        }
        anotarVizinho(curto, longo);
        Quadro q(Q_JOIN_RESP, curto);
        q.dados(longo, 8);
        q.u16(curto);
        q.dados(meuLongo, 8);
        q.enviar(canalDoAr());
        if (papel == ESP_ZB_DEVICE_TYPE_ROUTER) {
            esp_zb_zdo_signal_device_update_params_t p = {};
            p.short_addr = curto;
            memcpy(p.long_addr, longo, 8);
            novoSinal(ESP_ZB_ZDO_SIGNAL_DEVICE_UPDATE, ESP_OK, &p, sizeof(p));
        }
        break;
    }
    case Q_ANNCE: {
        esp_zb_zdo_signal_device_annce_params_t p = {};
        p.device_short_addr = d.u16();
        memcpy(p.ieee_addr, d.pular(8), 8);
        p.capability = d.u8();
        anotarVizinho(p.device_short_addr, p.ieee_addr);
        novoSinal(ESP_ZB_ZDO_SIGNAL_DEVICE_ANNCE, ESP_OK, &p, sizeof(p));
        break;
    }
    case Q_MATCH_REQ: {
        uint16_t perfil = d.u16();
        uint8_t entrada = d.u8(), saida = d.u8();
        std::vector<uint16_t> clusters;
        for (int i = 0; i < entrada + saida; i++) clusters.push_back(d.u16());
        for (esp_zb_ep_list_t *e = dispositivo; e; e = e->next) {
            if (!e->endpoint.ep_id || (perfil && e->endpoint.profile_id != perfil)) continue;
            bool bate = false;
            for (int i = 0; i < entrada; i++) bate = bate || clusterBate(e->endpoint, clusters[i], ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
            for (int i = entrada; i < entrada + saida; i++) bate = bate || clusterBate(e->endpoint, clusters[i], ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
            if (!bate) continue;
            Quadro q(Q_MATCH_RESP, origem, 0, 0, 0, numero);
            q.u8(e->endpoint.ep_id);
            q.dados(meuLongo, 8);
            q.enviar(canalDoAr());
        }
        break;
    }
    case Q_MATCH_RESP: {
        uint8_t ep = d.u8();
        anotarVizinho(origem, d.pular(8));
        for (PedidoDeMatch &m : matches) {
            if (m.tsn != numero || (m.respondido && !m.difusao)) continue;
            m.respondido = true;
            m.funcao(ESP_ZB_ZDP_STATUS_SUCCESS, origem, ep, m.contexto);
        }
        break;
    }
    case Q_BIND_REQ: {
        Ligacao l = {};
        const uint8_t *fonte = d.pular(8);
        l.epOrigem = d.u8();
        l.cluster = d.u16();
        l.modo = d.u8();
        memcpy(&l.destino, d.pular(8), 8);
        l.epDestino = d.u8();
        (void)fonte;
        const uint8_t *quem = d.pular(8);
        anotarVizinho(origem, quem);
        bool existe = false;
        for (const Ligacao &x : ligacoes)
            if (x.epOrigem == l.epOrigem && x.cluster == l.cluster && !memcmp(&x.destino, &l.destino, 8) && x.epDestino == l.epDestino) existe = true;
        if (!existe) ligacoes.push_back(l);
        Quadro q(Q_BIND_RESP, origem, 0, 0, 0, numero);
        q.u8(ESP_ZB_ZDP_STATUS_SUCCESS);
        q.enviar(canalDoAr());
        avisarAps(origem, 0x0021);
        break;
    }
    case Q_BIND_RESP: {
        uint8_t estado = d.u8();
        for (size_t i = 0; i < binds.size(); i++) {
            if (binds[i].tsn != numero) continue;
            PedidoDeBind b = binds[i];
            binds.erase(binds.begin() + (long)i);
            if (b.funcao) b.funcao((esp_zb_zdp_status_t)estado, b.contexto);
            break;
        }
        break;
    }
    case Q_ZCL: {
        uint8_t global = d.u8(), comando = d.u8(), direcao = d.u8();
        /* O endpoint 0xFF (o grupo) vale para todos os endpoints com o cluster. */
        if (epDestino == 0xFF) {
            for (esp_zb_ep_list_t *e = dispositivo; e; e = e->next)
                if (e->endpoint.ep_id && clusterBate(e->endpoint, cluster, direcao == ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI ? ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE : ESP_ZB_ZCL_CLUSTER_SERVER_ROLE)) {
                    if (global) tratarGlobal(origem, epOrigem, e->endpoint.ep_id, cluster, comando, direcao, numero, d);
                    else tratarComando(origem, epOrigem, e->endpoint.ep_id, cluster, comando, numero, d);
                }
            break;
        }
        if (global) tratarGlobal(origem, epOrigem, epDestino, cluster, comando, direcao, numero, d);
        else tratarComando(origem, epOrigem, epDestino, cluster, comando, numero, d);
        break;
    }
    case Q_LEAVE: {
        esp_zb_zdo_signal_leave_params_t p = {};
        p.leave_type = ESP_ZB_NWK_LEAVE_TYPE_RESET;
        novoSinal(ESP_ZB_ZDO_SIGNAL_LEAVE, ESP_OK, &p, sizeof(p));
        break;
    }
    default: break;
    }
}

/* =====================================================================
 * os relatorios automaticos */
void enviarRelatorio(Relatorio &r, esp_zb_zcl_attr_t *a)
{
    std::vector<uint8_t> saida;
    poeAtributo(saida, a->id, a->type, a->data_p);
    esp_zb_zcl_basic_cmd_t basico = {};
    basico.src_endpoint = r.info.ep;
    enviarZcl(basico, ESP_ZB_APS_ADDR_MODE_DST_ADDR_ENDP_NOT_PRESENT, r.info.cluster_id, true, ZCL_RELATORIO, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, saida.data(), saida.size());
    r.ultimo = numeroDoValor(a->type, a->data_p);
    r.enviado = true;
    r.quando = millis();
}

void olharRelatorios()
{
    for (Relatorio &r : relatorios) {
        esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(r.info.ep, r.info.cluster_id, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), r.info.attr_id);
        if (!a) continue;
        uint32_t passou = millis() - r.quando;
        /* 0xFFFF no maximo desliga; 0 no minimo e "assim que mudar". */
        if (r.maximo == 0xFFFF) continue;
        double valor = numeroDoValor(a->type, a->data_p);
        double delta = numeroDoValor(a->type, &r.info.u.send_info.delta);
        double mudou = valor > r.ultimo ? valor - r.ultimo : r.ultimo - valor;
        bool porMudanca = mudou > 0 && mudou >= delta && passou >= (uint32_t)r.minimo * 1000;
        bool porTempo = r.maximo && passou >= (uint32_t)r.maximo * 1000;
        if (!r.enviado || porMudanca || porTempo) {
            if (!r.enviado && !porTempo && !porMudanca) {
                /* Comeca a contar agora, com o valor de agora. */
                r.ultimo = valor;
                r.enviado = true;
                r.quando = millis();
                continue;
            }
            enviarRelatorio(r, a);
        }
    }
}

void darSinais()
{
    /* Os sinais podem criar outros: um por vez, na ordem. */
    for (int i = 0; i < 8 && !sinais.empty(); i++) {
        Sinal s = sinais.front();
        sinais.erase(sinais.begin());
        std::vector<uint8_t> memoria(sizeof(uint32_t) + s.parametros.size() + 8);
        memcpy(memoria.data(), &s.tipo, sizeof(uint32_t));
        if (!s.parametros.empty()) memcpy(memoria.data() + sizeof(uint32_t), s.parametros.data(), s.parametros.size());
        esp_zb_app_signal_t sinal;
        sinal.p_app_signal = (uint32_t *)memoria.data();
        sinal.esp_err_status = s.erro;
        esp_zb_app_signal_handler(&sinal);
    }
}

void darAlarmes()
{
    uint32_t agora = millis();
    for (size_t i = 0; i < alarmes.size();) {
        if ((int32_t)(agora - alarmes[i].quando) < 0) {
            i++;
            continue;
        }
        Alarme a = alarmes[i];
        alarmes.erase(alarmes.begin() + (long)i);
        a.funcao(a.parametro);
    }
}

void darPrazos()
{
    uint32_t agora = millis();
    for (size_t i = 0; i < matches.size();) {
        if ((int32_t)(agora - matches[i].prazo) < 0) {
            i++;
            continue;
        }
        PedidoDeMatch m = matches[i];
        matches.erase(matches.begin() + (long)i);
        if (!m.respondido) m.funcao(ESP_ZB_ZDP_STATUS_TIMEOUT, 0xFFFF, 0, m.contexto);
    }
    for (size_t i = 0; i < binds.size();) {
        if ((int32_t)(agora - binds[i].prazo) < 0) {
            i++;
            continue;
        }
        PedidoDeBind b = binds[i];
        binds.erase(binds.begin() + (long)i);
        if (b.funcao) b.funcao(ESP_ZB_ZDP_STATUS_TIMEOUT, b.contexto);
    }
    if (fimDaVarredura && (int32_t)(agora - prazoDaVarredura) >= 0) {
        esp_zb_zdo_scan_complete_callback_t f = fimDaVarredura;
        fimDaVarredura = nullptr;
        f(ESP_ZB_ZDP_STATUS_SUCCESS, (uint8_t)redesAchadas.size(), redesAchadas.empty() ? nullptr : redesAchadas.data());
    }
    if (procurando) {
        if ((int32_t)(agora - fimDaProcura) >= 0) {
            procurando = false;
            minhaPan = 0xFFFF;
            novoSinal(ESP_ZB_BDB_SIGNAL_STEERING, ESP_FAIL);
        } else if ((int32_t)(agora - proximoBeacon) >= 0) {
            proximoBeacon = agora + 200;
            Quadro q(Q_BEACON_REQ, 0xFFFF);
            q.enviar(CANAL_DE_TODOS);
        }
    }
}

void receberDoAr()
{
    static uint8_t entrada[1400];
    for (int i = 0; i < 16; i++) {
        int n = (int)cs_obj_i(nullptr, "__154_receber", cs_vetor_mutavel(entrada, (int)sizeof(entrada)));
        if (n <= 1) return;
        uint8_t canal = entrada[0];
        if (canal < INICIO_DOS_CANAIS) continue;
        if (canal == CANAL_DE_TODOS || !meuCanal || canal == canalDoAr()) tratarQuadro(entrada + 1, n - 1);
    }
}

uint8_t primeiroCanal(uint32_t mascara)
{
    for (uint8_t c = 11; c <= 26; c++)
        if (mascara & (1u << c)) return c;
    return 11;
}

uint8_t enviarMatch(esp_zb_zdo_match_desc_req_param_t *p, esp_zb_zdo_match_desc_callback_t funcao, void *contexto)
{
    uint8_t numero = ++tsn;
    PedidoDeMatch m = {funcao, contexto, millis() + 5000, p->dst_nwk_addr >= 0xFFF8, false, numero};
    matches.push_back(m);
    Quadro q(Q_MATCH_REQ, p->dst_nwk_addr, 0, 0, 0, numero);
    q.u16(p->profile_id);
    q.u8(p->num_in_clusters);
    q.u8(p->num_out_clusters);
    for (int i = 0; i < p->num_in_clusters + p->num_out_clusters; i++) q.u16(p->cluster_list[i]);
    q.enviar(canalDoAr());
    return numero;
}

} /* namespace */

/* =====================================================================
 * a API */
extern "C" {

/* ---- a pilha ---- */
esp_err_t esp_zb_platform_config(esp_zb_platform_config_t *config)
{
    (void)config;
    return ESP_OK;
}
void esp_zb_init(esp_zb_cfg_t *nwk_cfg)
{
    papel = nwk_cfg->esp_zb_role;
    meuEndereco();
    travar();
    destravar();
}
esp_err_t esp_zb_device_register(esp_zb_ep_list_t *ep_list)
{
    dispositivo = ep_list;
    return ESP_OK;
}
void esp_zb_core_action_handler_register(esp_zb_core_action_callback_t cb) { acao = cb; }
void esp_zb_aps_data_indication_handler_register(esp_zb_apsde_data_indication_callback_t cb) { indicacaoAps = cb; }
esp_err_t esp_zb_set_primary_network_channel_set(uint32_t channel_mask)
{
    mascaraPrimaria = channel_mask;
    return ESP_OK;
}
esp_err_t esp_zb_set_channel_mask(uint32_t channel_mask)
{
    mascaraGeral = channel_mask;
    return ESP_OK;
}
uint32_t esp_zb_get_channel_mask(void) { return mascaraGeral; }
void esp_zb_nvram_erase_at_start(bool erase) { (void)erase; }
void esp_zb_bdb_set_scan_duration(uint8_t duration) { duracaoDaVarredura = duration; }
esp_err_t esp_zb_start(bool autostart)
{
    cs_obj_n(nullptr, "__154_ligar", 1);
    ligado = true;
    if (autostart) novoSinal(ESP_ZB_BDB_SIGNAL_DEVICE_FIRST_START, ESP_OK);
    else novoSinal(ESP_ZB_ZDO_SIGNAL_SKIP_STARTUP, ESP_OK);
    return ESP_OK;
}
void esp_zb_stack_main_loop(void)
{
    for (;;) {
        travar();
        receberDoAr();
        darAlarmes();
        darPrazos();
        darSinais();
        olharRelatorios();
        destravar();
        delay(2);
    }
}
bool esp_zb_lock_acquire(TickType_t block_ticks)
{
    if (!travaDoZigbee) travaDoZigbee = xSemaphoreCreateRecursiveMutex();
    return xSemaphoreTakeRecursive(travaDoZigbee, block_ticks) == pdTRUE;
}
void esp_zb_lock_release(void) { xSemaphoreGiveRecursive(travaDoZigbee); }
void esp_zb_scheduler_alarm(esp_zb_callback_t cb, uint8_t param, uint32_t time)
{
    travar();
    alarmes.push_back({cb, param, millis() + time});
    destravar();
}
void *esp_zb_app_signal_get_params(uint32_t *signal_p) { return signal_p + 1; }
void esp_zb_factory_reset(void) { esp_restart(); }
void esp_zb_zcl_reset_nvram_to_factory_default(void) {}
void zb_set_ed_node_descriptor(bool power_src, bool rx_on_when_idle, bool alloc_addr)
{
    (void)power_src;
    (void)rx_on_when_idle;
    (void)alloc_addr;
}
zb_ret_t zb_nvram_write_dataset(zb_nvram_dataset_types_t t)
{
    (void)t;
    return 0;
}

/* ---- a rede ---- */
esp_err_t esp_zb_bdb_start_top_level_commissioning(uint8_t mode_mask)
{
    travar();
    if (mode_mask == ESP_ZB_BDB_MODE_INITIALIZATION) {
        novoSinal(ESP_ZB_BDB_SIGNAL_DEVICE_FIRST_START, ESP_OK);
    } else if (mode_mask & ESP_ZB_BDB_MODE_NETWORK_FORMATION) {
        minhaPan = (uint16_t)(esp_random() & 0x3FFF) + 1;
        esp_fill_random(minhaPanLonga, 8);
        meuCanal = primeiroCanal(mascaraPrimaria);
        meuCurto = 0x0000;
        naRede = true;
        novaDeFabrica = false;
        anotarVizinho(meuCurto, meuLongo);
        novoSinal(ESP_ZB_BDB_SIGNAL_FORMATION, ESP_OK);
    } else if (mode_mask & ESP_ZB_BDB_MODE_NETWORK_STEERING) {
        if (naRede) {
            /* A rede abre por 180 s, o padrao do BDB. */
            uint8_t tempo = 180;
            aberturaAte = millis() + tempo * 1000u;
            novoSinal(ESP_ZB_NWK_SIGNAL_PERMIT_JOIN_STATUS, ESP_OK, &tempo, 1);
            novoSinal(ESP_ZB_BDB_SIGNAL_STEERING, ESP_OK);
        } else {
            procurando = true;
            meuCanal = 0;
            proximoBeacon = millis();
            /* A varredura de cada canal da mascara, pelo tempo que o sketch pediu. */
            fimDaProcura = millis() + 1000u + 500u * duracaoDaVarredura;
        }
    }
    destravar();
    return ESP_OK;
}
bool esp_zb_bdb_is_factory_new(void) { return novaDeFabrica; }
bool esp_zb_bdb_dev_joined(void) { return naRede; }
esp_err_t esp_zb_bdb_open_network(uint8_t permit_duration)
{
    travar();
    aberturaAte = millis() + permit_duration * 1000u;
    novoSinal(ESP_ZB_NWK_SIGNAL_PERMIT_JOIN_STATUS, ESP_OK, &permit_duration, 1);
    destravar();
    return ESP_OK;
}
esp_err_t esp_zb_bdb_close_network(void)
{
    travar();
    uint8_t tempo = 0;
    aberturaAte = 0;
    novoSinal(ESP_ZB_NWK_SIGNAL_PERMIT_JOIN_STATUS, ESP_OK, &tempo, 1);
    destravar();
    return ESP_OK;
}
uint16_t esp_zb_get_pan_id(void) { return minhaPan; }
uint8_t esp_zb_get_current_channel(void) { return meuCanal; }
uint16_t esp_zb_get_short_address(void) { return meuCurto; }
void esp_zb_get_long_address(esp_zb_ieee_addr_t addr) { memcpy(addr, meuLongo, 8); }
void esp_zb_get_extended_pan_id(esp_zb_ieee_addr_t ext_pan_id) { memcpy(ext_pan_id, minhaPanLonga, 8); }
esp_err_t esp_zb_ieee_address_by_short(uint16_t short_addr, uint8_t *ieee_addr)
{
    for (const Vizinho &v : vizinhos)
        if (v.curto == short_addr) {
            memcpy(ieee_addr, v.longo, 8);
            return ESP_OK;
        }
    return ESP_ERR_NOT_FOUND;
}
void esp_zb_zdo_active_scan_request(uint32_t channel_mask, uint8_t scan_duration, esp_zb_zdo_scan_complete_callback_t user_cb)
{
    (void)channel_mask;
    travar();
    redesAchadas.clear();
    fimDaVarredura = user_cb;
    prazoDaVarredura = millis() + 300u + 100u * scan_duration;
    Quadro q(Q_BEACON_REQ, 0xFFFF);
    q.enviar(CANAL_DE_TODOS);
    destravar();
}

/* ---- ZDO: achar e ligar ---- */
esp_err_t esp_zb_zdo_match_cluster(esp_zb_zdo_match_desc_req_param_t *param, esp_zb_zdo_match_desc_callback_t user_cb, void *user_ctx)
{
    travar();
    enviarMatch(param, user_cb, user_ctx);
    destravar();
    return ESP_OK;
}
void esp_zb_zdo_device_bind_req(esp_zb_zdo_bind_req_param_t *cmd_req, esp_zb_zdo_bind_callback_t user_cb, void *user_ctx)
{
    travar();
    uint8_t numero = ++tsn;
    binds.push_back({user_cb, user_ctx, millis() + 5000, numero});
    Quadro q(Q_BIND_REQ, cmd_req->req_dst_addr, 0, 0, 0, numero);
    q.dados(cmd_req->src_address, 8);
    q.u8(cmd_req->src_endp);
    q.u16(cmd_req->cluster_id);
    q.u8(cmd_req->dst_addr_mode);
    q.dados(&cmd_req->dst_address_u, 8);
    q.u8(cmd_req->dst_endp);
    q.dados(meuLongo, 8);
    if (cmd_req->req_dst_addr == meuCurto) {
        /* O bind na propria tabela (o controlador liga o proprio endpoint ao outro). */
        Ligacao l = {};
        l.epOrigem = cmd_req->src_endp;
        l.cluster = cmd_req->cluster_id;
        l.modo = cmd_req->dst_addr_mode;
        memcpy(&l.destino, &cmd_req->dst_address_u, 8);
        l.epDestino = cmd_req->dst_endp;
        bool existe = false;
        for (const Ligacao &x : ligacoes)
            if (x.epOrigem == l.epOrigem && x.cluster == l.cluster && !memcmp(&x.destino, &l.destino, 8) && x.epDestino == l.epDestino) existe = true;
        if (!existe) ligacoes.push_back(l);
        binds.pop_back();
        if (user_cb) user_cb(ESP_ZB_ZDP_STATUS_SUCCESS, user_ctx);
    } else {
        q.enviar(canalDoAr());
    }
    destravar();
}
void esp_zb_zdo_binding_table_req(esp_zb_zdo_mgmt_bind_param_t *cmd_req, esp_zb_zdo_binding_table_callback_t user_cb, void *user_ctx)
{
    (void)cmd_req;
    travar();
    /* A tabela desta placa, numa lista so (o simulador nao pagina). */
    static std::vector<esp_zb_zdo_binding_table_record_t> registros;
    registros.clear();
    registros.reserve(ligacoes.size());
    for (const Ligacao &l : ligacoes) {
        esp_zb_zdo_binding_table_record_t r = {};
        memcpy(r.src_address, meuLongo, 8);
        r.src_endp = l.epOrigem;
        r.cluster_id = l.cluster;
        r.dst_addr_mode = l.modo;
        r.dst_address = l.destino;
        r.dst_endp = l.epDestino;
        registros.push_back(r);
    }
    for (size_t i = 0; i + 1 < registros.size(); i++) registros[i].next = &registros[i + 1];
    esp_zb_zdo_binding_table_info_t info = {};
    info.status = ESP_ZB_ZDP_STATUS_SUCCESS;
    info.index = 0;
    info.total = (uint8_t)registros.size();
    info.count = (uint8_t)registros.size();
    info.record = registros.empty() ? nullptr : registros.data();
    if (user_cb) user_cb(&info, user_ctx);
    destravar();
}

/* ---- os atributos ---- */
esp_zb_attribute_list_t *esp_zb_zcl_attr_list_create(uint16_t cluster_id) { return novaListaDeAtributos(cluster_id); }
esp_zb_cluster_list_t *esp_zb_zcl_cluster_list_create(void)
{
    auto *l = (esp_zb_cluster_list_t *)calloc(1, sizeof(esp_zb_cluster_list_t));
    l->cluster.cluster_id = 0xFFFF;
    return l;
}
esp_zb_ep_list_t *esp_zb_ep_list_create(void) { return (esp_zb_ep_list_t *)calloc(1, sizeof(esp_zb_ep_list_t)); }
esp_err_t esp_zb_ep_list_add_ep(esp_zb_ep_list_t *ep_list, esp_zb_cluster_list_t *cluster_list, esp_zb_endpoint_config_t endpoint_config)
{
    if (!ep_list) return ESP_ERR_INVALID_ARG;
    esp_zb_ep_list_t *fim = ep_list;
    while (fim->next) fim = fim->next;
    auto *novo = (esp_zb_ep_list_t *)calloc(1, sizeof(esp_zb_ep_list_t));
    novo->endpoint.ep_id = endpoint_config.endpoint;
    novo->endpoint.profile_id = endpoint_config.app_profile_id;
    novo->endpoint.cluster_list = cluster_list;
    fim->next = novo;
    return ESP_OK;
}
esp_err_t esp_zb_ep_list_add_gateway_ep(esp_zb_ep_list_t *ep_list, esp_zb_cluster_list_t *cluster_list, esp_zb_endpoint_config_t endpoint_config)
{
    return esp_zb_ep_list_add_ep(ep_list, cluster_list, endpoint_config);
}
esp_zb_attribute_list_t *esp_zb_cluster_list_get_cluster(const esp_zb_cluster_list_t *cluster_list, uint16_t cluster_id, uint8_t role_mask)
{
    for (const esp_zb_cluster_list_t *c = cluster_list; c; c = c->next)
        if (c->cluster.attr_list && c->cluster.cluster_id == cluster_id && (c->cluster.role_mask & role_mask)) return c->cluster.attr_list;
    return nullptr;
}
esp_err_t esp_zb_cluster_add_attr(esp_zb_attribute_list_t *attr_list, uint16_t cluster_id, uint16_t attr_id, uint8_t attr_type, uint8_t attr_access, void *value_p)
{
    return acrescentar(attr_list, cluster_id, attr_id, attr_type, attr_access, value_p);
}
esp_err_t esp_zb_custom_cluster_add_custom_attr(esp_zb_attribute_list_t *attr_list, uint16_t attr_id, uint8_t attr_type, uint8_t attr_access, void *value_p)
{
    return acrescentar(attr_list, attr_list ? attr_list->cluster_id : 0, attr_id, attr_type, attr_access, value_p);
}
esp_err_t esp_zb_cluster_update_attr(esp_zb_attribute_list_t *attr_list, uint16_t attr_id, void *value_p)
{
    esp_zb_zcl_attr_t *a = acharAtributo(attr_list, attr_id);
    if (!a) return ESP_ERR_NOT_FOUND;
    memcpy(a->data_p, value_p, tamanhoDoValor(a->type, value_p));
    return ESP_OK;
}
esp_zb_zcl_attr_t *esp_zb_zcl_get_attribute(uint8_t endpoint, uint16_t cluster_id, uint8_t cluster_role, uint16_t attr_id)
{
    return acharAtributo(acharCluster(endpoint, cluster_id, cluster_role), attr_id);
}
esp_zb_zcl_status_t esp_zb_zcl_set_attribute_val(uint8_t endpoint, uint16_t cluster_id, uint8_t cluster_role, uint16_t attr_id, void *value_p, bool check)
{
    (void)check;
    esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(endpoint, cluster_id, cluster_role), attr_id);
    if (!a) return ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB;
    memcpy(a->data_p, value_p, tamanhoDoValor(a->type, value_p));
    return ESP_ZB_ZCL_STATUS_SUCCESS;
}
uint16_t esp_zb_zcl_get_attribute_size(uint8_t attr_type, uint8_t *attr_value) { return tamanhoDoValor(attr_type, attr_value); }

/* ---- os relatorios ---- */
esp_err_t esp_zb_zcl_update_reporting_info(esp_zb_zcl_reporting_info_t *report_info)
{
    travar();
    Relatorio *r = nullptr;
    for (Relatorio &x : relatorios)
        if (x.info.ep == report_info->ep && x.info.cluster_id == report_info->cluster_id && x.info.attr_id == report_info->attr_id) r = &x;
    if (!r) {
        relatorios.push_back({});
        r = &relatorios.back();
    }
    r->info = *report_info;
    r->minimo = report_info->u.send_info.min_interval;
    r->maximo = report_info->u.send_info.max_interval;
    r->enviado = false;
    r->quando = millis();
    destravar();
    return ESP_OK;
}
esp_err_t esp_zb_zcl_report_attr_cmd_req(esp_zb_zcl_report_attr_cmd_t *cmd_req)
{
    uint16_t cluster = cmd_req->clusterID;
    esp_zb_zcl_attr_t *a = acharAtributo(acharCluster(cmd_req->zcl_basic_cmd.src_endpoint, cluster, ESP_ZB_ZCL_CLUSTER_SERVER_ROLE), cmd_req->attributeID);
    if (!a) return ESP_ERR_NOT_FOUND;
    std::vector<uint8_t> saida;
    poeAtributo(saida, a->id, a->type, a->data_p);
    enviarZcl(cmd_req->zcl_basic_cmd, cmd_req->address_mode, cluster, true, ZCL_RELATORIO, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, saida.data(), saida.size());
    return ESP_OK;
}
uint8_t esp_zb_zcl_config_report_cmd_req(esp_zb_zcl_config_report_cmd_t *cmd_req)
{
    std::vector<uint8_t> saida;
    for (int i = 0; i < cmd_req->record_number; i++) {
        const esp_zb_zcl_config_report_record_t &r = cmd_req->record_field[i];
        saida.push_back((uint8_t)r.attributeID);
        saida.push_back((uint8_t)(r.attributeID >> 8));
        saida.push_back(r.attrType);
        saida.push_back((uint8_t)r.min_interval);
        saida.push_back((uint8_t)(r.min_interval >> 8));
        saida.push_back((uint8_t)r.max_interval);
        saida.push_back((uint8_t)(r.max_interval >> 8));
        uint8_t delta[8] = {0};
        if (r.reportable_change) memcpy(delta, r.reportable_change, tamanhoDoValor(r.attrType, r.reportable_change) < 8 ? tamanhoDoValor(r.attrType, r.reportable_change) : 8);
        saida.insert(saida.end(), delta, delta + 8);
    }
    return enviarZcl(cmd_req->zcl_basic_cmd, cmd_req->address_mode, cmd_req->clusterID, true, ZCL_CONFIGURAR, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, saida.data(), saida.size());
}

/* ---- leitura e escrita pela rede ---- */
uint8_t esp_zb_zcl_read_attr_cmd_req(esp_zb_zcl_read_attr_cmd_t *cmd_req)
{
    std::vector<uint8_t> saida;
    for (int i = 0; i < cmd_req->attr_number; i++) {
        saida.push_back((uint8_t)cmd_req->attr_field[i]);
        saida.push_back((uint8_t)(cmd_req->attr_field[i] >> 8));
    }
    return enviarZcl(cmd_req->zcl_basic_cmd, cmd_req->address_mode, cmd_req->clusterID, true, ZCL_LER, cmd_req->direction, saida.data(), saida.size());
}
uint8_t esp_zb_zcl_write_attr_cmd_req(esp_zb_zcl_write_attr_cmd_t *cmd_req)
{
    std::vector<uint8_t> saida;
    for (int i = 0; i < cmd_req->attr_number; i++) {
        const esp_zb_zcl_attribute_t &a = cmd_req->attr_field[i];
        poeAtributo(saida, a.id, a.data.type, a.data.value);
    }
    return enviarZcl(cmd_req->zcl_basic_cmd, cmd_req->address_mode, cmd_req->clusterID, true, ZCL_ESCREVER, cmd_req->direction, saida.data(), saida.size());
}

/* ---- os comandos dos clusters ---- */
uint8_t esp_zb_zcl_on_off_cmd_req(esp_zb_zcl_on_off_cmd_t *c)
{
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_ONOFF, false, c->on_off_cmd_id, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, nullptr, 0);
}
uint8_t esp_zb_zcl_on_off_off_with_effect_cmd_req(esp_zb_zcl_on_off_off_with_effect_cmd_t *c)
{
    uint8_t d[2] = {c->effect_id, c->effect_variant};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_ONOFF, false, ESP_ZB_ZCL_CMD_ON_OFF_OFF_WITH_EFFECT_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 2);
}
uint8_t esp_zb_zcl_on_off_on_with_recall_global_scene_cmd_req(esp_zb_zcl_on_off_on_with_recall_global_scene_cmd_t *c)
{
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_ONOFF, false, ESP_ZB_ZCL_CMD_ON_OFF_ON_WITH_RECALL_GLOBAL_SCENE_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, nullptr, 0);
}
uint8_t esp_zb_zcl_on_off_on_with_timed_off_cmd_req(esp_zb_zcl_on_off_on_with_timed_off_cmd_t *c)
{
    uint8_t d[5] = {c->on_off_control, (uint8_t)c->on_time, (uint8_t)(c->on_time >> 8), (uint8_t)c->off_wait_time, (uint8_t)(c->off_wait_time >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_ONOFF, false, ESP_ZB_ZCL_CMD_ON_OFF_ON_WITH_TIMED_OFF_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 5);
}
uint8_t esp_zb_zcl_level_move_to_level_cmd_req(esp_zb_zcl_move_to_level_cmd_t *c)
{
    uint8_t d[3] = {c->level, (uint8_t)c->transition_time, (uint8_t)(c->transition_time >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_LEVEL, false, ESP_ZB_ZCL_CMD_LEVEL_CONTROL_MOVE_TO_LEVEL_WITH_ON_OFF, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 3);
}
uint8_t esp_zb_zcl_level_step_cmd_req(esp_zb_zcl_level_step_cmd_t *c)
{
    uint8_t d[4] = {c->step_mode, c->step_size, (uint8_t)c->transition_time, (uint8_t)(c->transition_time >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_LEVEL, false, ESP_ZB_ZCL_CMD_LEVEL_CONTROL_STEP, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 4);
}
uint8_t esp_zb_zcl_color_move_to_color_cmd_req(esp_zb_zcl_color_move_to_color_cmd_t *c)
{
    uint8_t d[6] = {(uint8_t)c->color_x, (uint8_t)(c->color_x >> 8), (uint8_t)c->color_y, (uint8_t)(c->color_y >> 8), (uint8_t)c->transition_time, (uint8_t)(c->transition_time >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_COLOR, false, ESP_ZB_ZCL_CMD_COLOR_CONTROL_MOVE_TO_COLOR, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 6);
}
uint8_t esp_zb_zcl_ias_zone_status_change_notif_cmd_req(esp_zb_zcl_ias_zone_status_change_notif_cmd_t *c)
{
    uint8_t d[6] = {(uint8_t)c->zone_status, (uint8_t)(c->zone_status >> 8), c->extend_status, c->zone_id, (uint8_t)c->delay, (uint8_t)(c->delay >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_IAS, false, ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_STATUS_CHANGE_NOT_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, d, 6);
}
uint8_t esp_zb_zcl_ias_zone_enroll_cmd_req(esp_zb_zcl_ias_zone_enroll_request_cmd_t *c)
{
    uint8_t d[4] = {(uint8_t)c->zone_type, (uint8_t)(c->zone_type >> 8), (uint8_t)c->manuf_code, (uint8_t)(c->manuf_code >> 8)};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_IAS, false, ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_ENROLL_REQUEST_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_CLI, d, 4);
}
uint8_t esp_zb_zcl_ias_zone_enroll_cmd_resp(esp_zb_zcl_ias_zone_enroll_response_cmd_t *c)
{
    uint8_t d[2] = {c->enroll_rsp_code, c->zone_id};
    return enviarZcl(c->zcl_basic_cmd, c->address_mode, C_IAS, false, ESP_ZB_ZCL_CMD_IAS_ZONE_ZONE_ENROLL_RESPONSE_ID, ESP_ZB_ZCL_CMD_DIRECTION_TO_SRV, d, 2);
}
esp_err_t esp_zb_zcl_add_privilege_command(uint8_t endpoint, uint16_t cluster, uint16_t command)
{
    (void)endpoint;
    (void)cluster;
    (void)command;
    return ESP_OK;
}
/* O OTA pelo Zigbee: o servidor nao existe na bancada. */
esp_err_t esp_zb_ota_upgrade_client_query_image_req(uint16_t server_ep, uint8_t server_addr)
{
    (void)server_ep;
    (void)server_addr;
    return ESP_OK;
}
esp_err_t esp_zb_ota_upgrade_client_query_interval_set(uint8_t endpoint, uint16_t interval)
{
    (void)endpoint;
    (void)interval;
    return ESP_OK;
}
void esp_zb_zcl_multi_input_init_server(void) {}
void esp_zb_zcl_multi_input_init_client(void) {}

/* ---- os textos ---- */
const char *esp_zb_zdo_signal_to_string(esp_zb_app_signal_type_t signal)
{
    switch (signal) {
    case ESP_ZB_ZDO_SIGNAL_SKIP_STARTUP: return "ZDO skip startup";
    case ESP_ZB_BDB_SIGNAL_DEVICE_FIRST_START: return "BDB device first start";
    case ESP_ZB_BDB_SIGNAL_DEVICE_REBOOT: return "BDB device reboot";
    case ESP_ZB_BDB_SIGNAL_FORMATION: return "BDB formation";
    case ESP_ZB_BDB_SIGNAL_STEERING: return "BDB steering";
    case ESP_ZB_ZDO_SIGNAL_DEVICE_ANNCE: return "ZDO device announce";
    case ESP_ZB_ZDO_SIGNAL_DEVICE_UPDATE: return "ZDO device update";
    case ESP_ZB_NWK_SIGNAL_PERMIT_JOIN_STATUS: return "NWK permit join status";
    case ESP_ZB_ZDO_SIGNAL_LEAVE: return "ZDO leave";
    default: return "signal";
    }
}
const char *esp_zb_zcl_status_to_name(esp_zb_zcl_status_t status)
{
    switch (status) {
    case ESP_ZB_ZCL_STATUS_SUCCESS: return "ESP_ZB_ZCL_STATUS_SUCCESS";
    case ESP_ZB_ZCL_STATUS_FAIL: return "ESP_ZB_ZCL_STATUS_FAIL";
    case ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB: return "ESP_ZB_ZCL_STATUS_UNSUP_ATTRIB";
    case ESP_ZB_ZCL_STATUS_INVALID_TYPE: return "ESP_ZB_ZCL_STATUS_INVALID_TYPE";
    case ESP_ZB_ZCL_STATUS_READ_ONLY: return "ESP_ZB_ZCL_STATUS_READ_ONLY";
    default: return "ESP_ZB_ZCL_STATUS";
    }
}

/* =====================================================================
 * criar os clusters: os atributos obrigatorios, com o valor da configuracao */
#define CS_LISTA(nome, cluster)                                                                                                                                                         \
    esp_err_t esp_zb_cluster_list_add_##nome##_cluster(esp_zb_cluster_list_t *cluster_list, esp_zb_attribute_list_t *attr_list, uint8_t role_mask)                                      \
    {                                                                                                                                                                                   \
        return acrescentarCluster(cluster_list, attr_list, cluster, role_mask);                                                                                                         \
    }
#define CS_ADD(nome)                                                                                                                                                                    \
    esp_err_t esp_zb_##nome##_cluster_add_attr(esp_zb_attribute_list_t *attr_list, uint16_t attr_id, void *value_p) { return acrescentarDaTabela(attr_list, attr_id, value_p); }

CS_LISTA(basic, C_BASIC) CS_ADD(basic)
CS_LISTA(power_config, C_POWER) CS_ADD(power_config)
CS_LISTA(identify, C_IDENT) CS_ADD(identify)
CS_LISTA(groups, C_GROUPS) CS_ADD(groups)
CS_LISTA(scenes, C_SCENES) CS_ADD(scenes)
CS_LISTA(on_off, C_ONOFF) CS_ADD(on_off)
CS_LISTA(level, C_LEVEL) CS_ADD(level)
CS_LISTA(time, C_TIME) CS_ADD(time)
CS_LISTA(color_control, C_COLOR) CS_ADD(color_control)
CS_LISTA(analog_input, C_AIN) CS_ADD(analog_input)
CS_LISTA(analog_output, C_AOUT) CS_ADD(analog_output)
CS_LISTA(binary_input, C_BIN) CS_ADD(binary_input)
CS_LISTA(binary_output, C_BOUT) CS_ADD(binary_output)
CS_LISTA(multistate_input, C_MIN) CS_ADD(multistate_input)
CS_LISTA(multistate_output, C_MOUT) CS_ADD(multistate_output)
CS_LISTA(window_covering, C_WIN) CS_ADD(window_covering)
CS_LISTA(thermostat, C_THERM) CS_ADD(thermostat)
CS_LISTA(fan_control, C_FAN) CS_ADD(fan_control)
CS_LISTA(illuminance_meas, C_LUX) CS_ADD(illuminance_meas)
CS_LISTA(temperature_meas, C_TEMP) CS_ADD(temperature_meas)
CS_LISTA(pressure_meas, C_PRESS) CS_ADD(pressure_meas)
CS_LISTA(flow_meas, C_FLOW) CS_ADD(flow_meas)
CS_LISTA(humidity_meas, C_HUM) CS_ADD(humidity_meas)
CS_LISTA(occupancy_sensing, C_OCC) CS_ADD(occupancy_sensing)
CS_LISTA(ias_zone, C_IAS) CS_ADD(ias_zone)
CS_LISTA(electrical_meas, C_ELEC) CS_ADD(electrical_meas)
CS_LISTA(carbon_dioxide_measurement, C_CO2) CS_ADD(carbon_dioxide_measurement)
CS_LISTA(pm2_5_measurement, C_PM25) CS_ADD(pm2_5_measurement)
CS_LISTA(wind_speed_measurement, C_WIND) CS_ADD(wind_speed_measurement)
CS_LISTA(ota, C_OTA) CS_ADD(ota)

esp_zb_attribute_list_t *esp_zb_basic_cluster_create(esp_zb_basic_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_BASIC);
    uint8_t versao = cfg ? cfg->zcl_version : 8, fonte = cfg ? cfg->power_source : 0;
    acrescentarDaTabela(l, 0x0000, &versao);
    acrescentarDaTabela(l, 0x0007, &fonte);
    return l;
}
esp_zb_attribute_list_t *esp_zb_identify_cluster_create(esp_zb_identify_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_IDENT);
    uint16_t t = cfg ? cfg->identify_time : 0;
    acrescentarDaTabela(l, 0x0000, &t);
    return l;
}
esp_zb_attribute_list_t *esp_zb_groups_cluster_create(esp_zb_groups_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_GROUPS);
    uint8_t v = cfg ? cfg->groups_name_support_id : 0;
    acrescentarDaTabela(l, 0x0000, &v);
    return l;
}
esp_zb_attribute_list_t *esp_zb_scenes_cluster_create(esp_zb_scenes_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_SCENES);
    esp_zb_scenes_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    uint8_t valido = c.scene_valid;
    acrescentarDaTabela(l, 0x0000, &c.scenes_count);
    acrescentarDaTabela(l, 0x0001, &c.current_scene);
    acrescentarDaTabela(l, 0x0002, &c.current_group);
    acrescentarDaTabela(l, 0x0003, &valido);
    acrescentarDaTabela(l, 0x0004, &c.name_support);
    return l;
}
esp_zb_attribute_list_t *esp_zb_on_off_cluster_create(esp_zb_on_off_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_ONOFF);
    uint8_t v = cfg ? cfg->on_off : 0;
    acrescentarDaTabela(l, 0x0000, &v);
    return l;
}
esp_zb_attribute_list_t *esp_zb_level_cluster_create(esp_zb_level_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_LEVEL);
    uint8_t v = cfg ? cfg->current_level : 0;
    acrescentarDaTabela(l, 0x0000, &v);
    return l;
}
esp_zb_attribute_list_t *esp_zb_color_control_cluster_create(esp_zb_color_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_COLOR);
    esp_zb_color_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0003, &c.current_x);
    acrescentarDaTabela(l, 0x0004, &c.current_y);
    acrescentarDaTabela(l, 0x0008, &c.color_mode);
    acrescentarDaTabela(l, 0x000F, &c.options);
    acrescentarDaTabela(l, 0x4001, &c.enhanced_color_mode);
    acrescentarDaTabela(l, 0x400A, &c.color_capabilities);
    return l;
}
#define CS_MEDIDA(nome, cluster, tipo, cfg_t)                                                                                                                                           \
    esp_zb_attribute_list_t *esp_zb_##nome##_cluster_create(cfg_t *cfg)                                                                                                                  \
    {                                                                                                                                                                                   \
        auto *l = novaListaDeAtributos(cluster);                                                                                                                                         \
        tipo v[3] = {};                                                                                                                                                                  \
        if (cfg) memcpy(v, cfg, sizeof(v));                                                                                                                                              \
        acrescentarDaTabela(l, 0x0000, &v[0]);                                                                                                                                           \
        acrescentarDaTabela(l, 0x0001, &v[1]);                                                                                                                                           \
        acrescentarDaTabela(l, 0x0002, &v[2]);                                                                                                                                           \
        return l;                                                                                                                                                                        \
    }
CS_MEDIDA(temperature_meas, C_TEMP, int16_t, esp_zb_temperature_meas_cluster_cfg_t)
CS_MEDIDA(humidity_meas, C_HUM, uint16_t, esp_zb_humidity_meas_cluster_cfg_t)
CS_MEDIDA(pressure_meas, C_PRESS, int16_t, esp_zb_pressure_meas_cluster_cfg_t)
CS_MEDIDA(flow_meas, C_FLOW, uint16_t, esp_zb_flow_meas_cluster_cfg_t)
CS_MEDIDA(illuminance_meas, C_LUX, uint16_t, esp_zb_illuminance_meas_cluster_cfg_t)
CS_MEDIDA(carbon_dioxide_measurement, C_CO2, float, esp_zb_carbon_dioxide_measurement_cluster_cfg_t)
CS_MEDIDA(pm2_5_measurement, C_PM25, float, esp_zb_pm2_5_measurement_cluster_cfg_t)
CS_MEDIDA(wind_speed_measurement, C_WIND, uint16_t, esp_zb_wind_speed_measurement_cluster_cfg_t)

esp_zb_attribute_list_t *esp_zb_occupancy_sensing_cluster_create(esp_zb_occupancy_sensing_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_OCC);
    esp_zb_occupancy_sensing_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    uint8_t tipo = (uint8_t)c.sensor_type;
    acrescentarDaTabela(l, 0x0000, &c.occupancy);
    acrescentarDaTabela(l, 0x0001, &tipo);
    acrescentarDaTabela(l, 0x0002, &c.sensor_type_bitmap);
    return l;
}
esp_zb_attribute_list_t *esp_zb_ias_zone_cluster_create(esp_zb_ias_zone_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_IAS);
    esp_zb_ias_zone_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0000, &c.zone_state);
    acrescentarDaTabela(l, 0x0001, &c.zone_type);
    acrescentarDaTabela(l, 0x0002, &c.zone_status);
    acrescentarDaTabela(l, 0x0010, c.ias_cie_addr);
    acrescentarDaTabela(l, 0x0011, &c.zone_id);
    return l;
}
esp_zb_attribute_list_t *esp_zb_thermostat_cluster_create(esp_zb_thermostat_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_THERM);
    esp_zb_thermostat_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0000, &c.local_temperature);
    acrescentarDaTabela(l, 0x0011, &c.occupied_cooling_setpoint);
    acrescentarDaTabela(l, 0x0012, &c.occupied_heating_setpoint);
    acrescentarDaTabela(l, 0x001B, &c.control_sequence_of_operation);
    acrescentarDaTabela(l, 0x001C, &c.system_mode);
    return l;
}
esp_zb_attribute_list_t *esp_zb_fan_control_cluster_create(esp_zb_fan_control_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_FAN);
    esp_zb_fan_control_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0000, &c.fan_mode);
    acrescentarDaTabela(l, 0x0001, &c.fan_mode_sequence);
    return l;
}
#define CS_ANALOGICO(nome, cluster, cfg_t)                                                                                                                                              \
    esp_zb_attribute_list_t *esp_zb_##nome##_cluster_create(cfg_t *cfg)                                                                                                                  \
    {                                                                                                                                                                                   \
        auto *l = novaListaDeAtributos(cluster);                                                                                                                                         \
        cfg_t c = {};                                                                                                                                                                    \
        if (cfg) c = *cfg;                                                                                                                                                               \
        uint8_t fora = c.out_of_service;                                                                                                                                                 \
        acrescentarDaTabela(l, 0x0051, &fora);                                                                                                                                           \
        acrescentarDaTabela(l, 0x0055, &c.present_value);                                                                                                                                \
        acrescentarDaTabela(l, 0x006F, &c.status_flags);                                                                                                                                 \
        return l;                                                                                                                                                                        \
    }
CS_ANALOGICO(analog_input, C_AIN, esp_zb_analog_input_cluster_cfg_t)
CS_ANALOGICO(analog_output, C_AOUT, esp_zb_analog_output_cluster_cfg_t)
#define CS_BINARIO(nome, cluster, cfg_t)                                                                                                                                                \
    esp_zb_attribute_list_t *esp_zb_##nome##_cluster_create(cfg_t *cfg)                                                                                                                  \
    {                                                                                                                                                                                   \
        auto *l = novaListaDeAtributos(cluster);                                                                                                                                         \
        cfg_t c = {};                                                                                                                                                                    \
        if (cfg) c = *cfg;                                                                                                                                                               \
        uint8_t fora = c.out_of_service, valor = c.present_value;                                                                                                                        \
        acrescentarDaTabela(l, 0x0051, &fora);                                                                                                                                           \
        acrescentarDaTabela(l, 0x0055, &valor);                                                                                                                                          \
        acrescentarDaTabela(l, 0x006F, &c.status_flags);                                                                                                                                 \
        return l;                                                                                                                                                                        \
    }
CS_BINARIO(binary_input, C_BIN, esp_zb_binary_input_cluster_cfg_t)
CS_BINARIO(binary_output, C_BOUT, esp_zb_binary_output_cluster_cfg_t)
#define CS_MULTIESTADO(nome, cluster, cfg_t)                                                                                                                                            \
    esp_zb_attribute_list_t *esp_zb_##nome##_cluster_create(cfg_t *cfg)                                                                                                                  \
    {                                                                                                                                                                                   \
        auto *l = novaListaDeAtributos(cluster);                                                                                                                                         \
        cfg_t c = {};                                                                                                                                                                    \
        if (cfg) c = *cfg;                                                                                                                                                               \
        uint8_t fora = c.out_of_service;                                                                                                                                                 \
        uint16_t valor = (uint16_t)c.present_value;                                                                                                                                      \
        acrescentarDaTabela(l, 0x004A, &c.number_of_states);                                                                                                                             \
        acrescentarDaTabela(l, 0x0051, &fora);                                                                                                                                           \
        acrescentarDaTabela(l, 0x0055, &valor);                                                                                                                                          \
        acrescentarDaTabela(l, 0x006F, &c.status_flags);                                                                                                                                 \
        return l;                                                                                                                                                                        \
    }
CS_MULTIESTADO(multistate_input, C_MIN, esp_zb_multistate_input_cluster_cfg_t)
CS_MULTIESTADO(multistate_output, C_MOUT, esp_zb_multistate_output_cluster_cfg_t)

esp_zb_attribute_list_t *esp_zb_window_covering_cluster_create(esp_zb_window_covering_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_WIN);
    esp_zb_window_covering_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0000, &c.covering_type);
    acrescentarDaTabela(l, 0x0007, &c.covering_status);
    acrescentarDaTabela(l, 0x0017, &c.covering_mode);
    return l;
}
esp_zb_attribute_list_t *esp_zb_electrical_meas_cluster_create(esp_zb_electrical_meas_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_ELEC);
    uint32_t t = cfg ? cfg->measured_type : 0;
    acrescentarDaTabela(l, 0x0000, &t);
    return l;
}
esp_zb_attribute_list_t *esp_zb_ota_cluster_create(esp_zb_ota_cluster_cfg_t *cfg)
{
    auto *l = novaListaDeAtributos(C_OTA);
    esp_zb_ota_cluster_cfg_t c = {};
    if (cfg) c = *cfg;
    acrescentarDaTabela(l, 0x0000, c.ota_upgrade_server_id);
    acrescentarDaTabela(l, 0x0002, &c.ota_upgrade_file_version);
    acrescentarDaTabela(l, 0x0006, &c.ota_image_upgrade_status);
    return l;
}

/* ---- os dispositivos do HA: os clusters que a especificacao pede em cada um ---- */
esp_zb_cluster_list_t *esp_zb_on_off_light_clusters_create(esp_zb_on_off_light_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_groups_cluster(l, esp_zb_groups_cluster_create(&cfg->groups_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_scenes_cluster(l, esp_zb_scenes_cluster_create(&cfg->scenes_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_on_off_cluster(l, esp_zb_on_off_cluster_create(&cfg->on_off_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_mains_power_outlet_clusters_create(esp_zb_mains_power_outlet_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_groups_cluster(l, esp_zb_groups_cluster_create(&cfg->groups_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_scenes_cluster(l, esp_zb_scenes_cluster_create(&cfg->scenes_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_on_off_cluster(l, esp_zb_on_off_cluster_create(&cfg->on_off_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_on_off_switch_clusters_create(esp_zb_on_off_switch_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_zcl_attr_list_create(C_IDENT), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    esp_zb_cluster_list_add_on_off_cluster(l, esp_zb_on_off_cluster_create(nullptr), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_color_dimmable_light_clusters_create(esp_zb_color_dimmable_light_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_groups_cluster(l, esp_zb_groups_cluster_create(&cfg->groups_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_scenes_cluster(l, esp_zb_scenes_cluster_create(&cfg->scenes_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_on_off_cluster(l, esp_zb_on_off_cluster_create(&cfg->on_off_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_level_cluster(l, esp_zb_level_cluster_create(&cfg->level_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_color_control_cluster(l, esp_zb_color_control_cluster_create(&cfg->color_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_color_dimmable_switch_clusters_create(esp_zb_color_dimmable_switch_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_zcl_attr_list_create(C_IDENT), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    esp_zb_cluster_list_add_on_off_cluster(l, esp_zb_on_off_cluster_create(nullptr), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    esp_zb_cluster_list_add_level_cluster(l, esp_zb_level_cluster_create(nullptr), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    esp_zb_cluster_list_add_color_control_cluster(l, esp_zb_color_control_cluster_create(nullptr), ESP_ZB_ZCL_CLUSTER_CLIENT_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_temperature_sensor_clusters_create(esp_zb_temperature_sensor_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_temperature_meas_cluster(l, esp_zb_temperature_meas_cluster_create(&cfg->temp_meas_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    return l;
}
esp_zb_cluster_list_t *esp_zb_light_sensor_clusters_create(esp_zb_light_sensor_cfg_t *cfg)
{
    auto *l = esp_zb_zcl_cluster_list_create();
    esp_zb_cluster_list_add_basic_cluster(l, esp_zb_basic_cluster_create(&cfg->basic_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_identify_cluster(l, esp_zb_identify_cluster_create(&cfg->identify_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    esp_zb_cluster_list_add_illuminance_meas_cluster(l, esp_zb_illuminance_meas_cluster_create(&cfg->illuminance_cfg), ESP_ZB_ZCL_CLUSTER_SERVER_ROLE);
    return l;
}

} /* extern "C" */
