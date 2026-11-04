/* cs_openthread.cpp -- a plataforma do OpenThread no simulador (a C6 e a H2).
 *
 * A pilha (nativo/openthread/src/core) e a de verdade; aqui fica o que na placa
 * e o chip: o radio 802.15.4, que manda os quadros para as outras placas da
 * bancada (motor/Rede154.cs), com o filtro de PAN e endereco e o ACK que o
 * hardware faz; os relogios, o sorteio, a memoria; e o esp_openthread_* do
 * IDF, que liga a pilha numa tarefa do FreeRTOS. A logica do radio segue a
 * plataforma de simulacao do proprio OpenThread (examples/platforms/simulation).
 *
 * As configuracoes de rede ficam na memoria (settings_ram.c): reiniciar a
 * placa simulada esquece o dataset, que na placa fica na flash. */
#include "Arduino.h"
#include "cs_objeto.h"
#include "esp_openthread.h"
#include "esp_openthread_lock.h"
#include "esp_openthread_netif_glue.h"
#include "esp_openthread_cli.h"
#include "esp_netif.h"
#include "esp_netif_net_stack.h"
#include "esp_event.h"
#include "soc/soc_caps.h"
#include "freertos/semphr.h"
#include <openthread/cli.h>
#include <openthread/dataset.h>
#include <openthread/instance.h>
#include <openthread/ip6.h>
#include <openthread/tasklet.h>
#include <openthread/thread.h>
#include <openthread/platform/diag.h>
#include <openthread/platform/alarm-micro.h>
#include <openthread/platform/alarm-milli.h>
#include <openthread/platform/entropy.h>
#include <openthread/platform/logging.h>
#include <openthread/platform/memory.h>
#include <openthread/platform/misc.h>
#include <openthread/platform/radio.h>
#include <openthread/platform/time.h>
#include "utils/mac_frame.h"
#include "esp_random.h"
#include <stdarg.h>
#include <vector>

/* =====================================================================
 * o radio */
namespace {

const uint32_t ESPERA_DO_ACK_US = 16000;
const int8_t RSSI_DA_BANCADA = -40;

otRadioState estado = OT_RADIO_STATE_DISABLED;
otPanId meuPan = 0xFFFF;
otExtAddress meuExt = {};
otShortAddress meuCurto = 0xFFFF, meuCurtoAlternativo = 0xFFFE;
uint8_t canal = 11;
bool promiscuo = false, ligadoNoOcio = true, comparaOrigem = false;
int8_t potencia = 20;

uint8_t pduTx[OT_RADIO_FRAME_MAX_SIZE], pduRx[OT_RADIO_FRAME_MAX_SIZE], pduAck[OT_RADIO_FRAME_MAX_SIZE], entrada[OT_RADIO_FRAME_MAX_SIZE + 1];
otRadioFrame quadroTx, quadroRx, quadroAck;
bool enviarAgora = false, esperandoAck = false, varrendo = false;
uint32_t ackDesde = 0, fimDaVarredura = 0;
std::vector<otShortAddress> origensCurtas;
std::vector<otExtAddress> origensLongas;

void mandarNoAr(uint8_t ch, const uint8_t *psdu, uint16_t n) { cs_obj_n(nullptr, "__154_enviar", (int)ch, cs_vetor(psdu, (int)n)); }

/* O ACK de um quadro que veio com o bit: o "frame pending" sai da tabela de origens. */
void responderAck(otInstance *instancia)
{
    (void)instancia;
    bool pendente = true;
    if (comparaOrigem) {
        pendente = false;
        otMacAddress origem;
        if (otMacFrameGetSrcAddr(&quadroRx, &origem) == OT_ERROR_NONE) {
            if (origem.mType == OT_MAC_ADDRESS_TYPE_SHORT) {
                for (otShortAddress a : origensCurtas)
                    if (a == origem.mAddress.mShortAddress) pendente = true;
            } else if (origem.mType == OT_MAC_ADDRESS_TYPE_EXTENDED) {
                for (const otExtAddress &a : origensLongas)
                    if (!memcmp(a.m8, origem.mAddress.mExtAddress.m8, sizeof(a.m8))) pendente = true;
            }
        }
    }
    quadroAck.mChannel = canal;
    if (otMacFrameIsVersion2015(&quadroRx)) {
        if (otMacFrameGenerateEnhAck(&quadroRx, pendente, nullptr, 0, &quadroAck) != OT_ERROR_NONE) return;
    } else {
        otMacFrameGenerateImmAck(&quadroRx, pendente, &quadroAck);
    }
    mandarNoAr(canal, quadroAck.mPsdu, quadroAck.mLength);
}

uint8_t sequencia(const otRadioFrame *q)
{
    uint8_t n = 0;
    otMacFrameGetSequence(q, &n);
    return n;
}

void receberDoAr(otInstance *instancia)
{
    for (int i = 0; i < 16; i++) {
        int n = (int)cs_obj_i(nullptr, "__154_receber", cs_vetor_mutavel(entrada, (int)sizeof(entrada)));
        if (n <= 1) return;
        uint8_t chDoQuadro = entrada[0];
        if (estado == OT_RADIO_STATE_DISABLED || estado == OT_RADIO_STATE_SLEEP || chDoQuadro != canal) continue;
        quadroRx.mLength = (uint16_t)(n - 1);
        quadroRx.mChannel = chDoQuadro;
        memcpy(quadroRx.mPsdu, entrada + 1, quadroRx.mLength);
        quadroRx.mInfo.mRxInfo.mRssi = RSSI_DA_BANCADA;
        quadroRx.mInfo.mRxInfo.mLqi = 255;
        quadroRx.mInfo.mRxInfo.mTimestamp = otPlatTimeGet();
        quadroRx.mInfo.mRxInfo.mAckedWithFramePending = false;
        quadroRx.mInfo.mRxInfo.mAckedWithSecEnhAck = false;

        /* O ACK do quadro que esta maquina mandou. */
        if (esperandoAck) {
            if (otMacFrameIsAck(&quadroRx) && sequencia(&quadroRx) == sequencia(&quadroTx)) {
                esperandoAck = false;
                estado = OT_RADIO_STATE_RECEIVE;
                otPlatRadioTxDone(instancia, &quadroTx, &quadroRx, OT_ERROR_NONE);
            }
            continue;
        }
        if (estado != OT_RADIO_STATE_RECEIVE || otMacFrameIsAck(&quadroRx)) continue;
        /* O filtro de PAN e endereco que o hardware faz. */
        if (!promiscuo && !otMacFrameDoesAddrMatchAny(&quadroRx, meuPan, meuCurto, meuCurtoAlternativo, &meuExt)) continue;
        if (otMacFrameIsAckRequested(&quadroRx)) {
            responderAck(instancia);
            quadroRx.mInfo.mRxInfo.mAckedWithFramePending = true;
        }
        otPlatRadioReceiveDone(instancia, &quadroRx, OT_ERROR_NONE);
    }
}

void processarRadio(otInstance *instancia)
{
    if (enviarAgora) {
        enviarAgora = false;
        mandarNoAr(quadroTx.mChannel, quadroTx.mPsdu, quadroTx.mLength);
        otPlatRadioTxStarted(instancia, &quadroTx);
        if (otMacFrameIsAckRequested(&quadroTx)) {
            esperandoAck = true;
            ackDesde = micros();
        } else {
            estado = OT_RADIO_STATE_RECEIVE;
            otPlatRadioTxDone(instancia, &quadroTx, nullptr, OT_ERROR_NONE);
        }
    }
    receberDoAr(instancia);
    if (esperandoAck && micros() - ackDesde > ESPERA_DO_ACK_US) {
        esperandoAck = false;
        estado = OT_RADIO_STATE_RECEIVE;
        otPlatRadioTxDone(instancia, &quadroTx, nullptr, OT_ERROR_NO_ACK);
    }
    if (varrendo && (int32_t)(millis() - fimDaVarredura) >= 0) {
        varrendo = false;
        otPlatRadioEnergyScanDone(instancia, RSSI_DA_BANCADA);
    }
}

/* ---- os relogios ---- */
bool alarmeMilli = false, alarmeMicro = false;
uint32_t milliT0, milliDt, microT0, microDt;

void processarAlarmes(otInstance *instancia)
{
    if (alarmeMilli && millis() - milliT0 >= milliDt) {
        alarmeMilli = false;
        otPlatAlarmMilliFired(instancia);
    }
    if (alarmeMicro && micros() - microT0 >= microDt) {
        alarmeMicro = false;
        otPlatAlarmMicroFired(instancia);
    }
}

} /* namespace */

extern "C" {

uint64_t otPlatTimeGet(void) { return (uint64_t)micros(); }
uint16_t otPlatTimeGetXtalAccuracy(void) { return 0; }

otRadioCaps otPlatRadioGetCaps(otInstance *aInstance)
{
    (void)aInstance;
    return (otRadioCaps)(OT_RADIO_CAPS_ACK_TIMEOUT | OT_RADIO_CAPS_CSMA_BACKOFF | OT_RADIO_CAPS_ENERGY_SCAN);
}
const char *otPlatRadioGetVersionString(otInstance *aInstance) { return otGetVersionString(); }
int8_t otPlatRadioGetReceiveSensitivity(otInstance *aInstance)
{
    (void)aInstance;
    return -100;
}
void otPlatRadioGetIeeeEui64(otInstance *aInstance, uint8_t *aIeeeEui64)
{
    (void)aInstance;
    uint64_t mac = ESP.getEfuseMac();
    aIeeeEui64[0] = (uint8_t)mac;
    aIeeeEui64[1] = (uint8_t)(mac >> 8);
    aIeeeEui64[2] = (uint8_t)(mac >> 16);
    aIeeeEui64[3] = 0xFF;
    aIeeeEui64[4] = 0xFE;
    aIeeeEui64[5] = (uint8_t)(mac >> 24);
    aIeeeEui64[6] = (uint8_t)(mac >> 32);
    aIeeeEui64[7] = (uint8_t)(mac >> 40);
}
void otPlatRadioSetPanId(otInstance *aInstance, otPanId aPanId)
{
    (void)aInstance;
    meuPan = aPanId;
}
void otPlatRadioSetExtendedAddress(otInstance *aInstance, const otExtAddress *aExtAddress)
{
    (void)aInstance;
    /* O OpenThread entrega o endereco invertido (a ordem do ar). */
    for (size_t i = 0; i < sizeof(meuExt.m8); i++) meuExt.m8[i] = aExtAddress->m8[sizeof(meuExt.m8) - 1 - i];
}
void otPlatRadioSetShortAddress(otInstance *aInstance, otShortAddress aShortAddress)
{
    (void)aInstance;
    meuCurto = aShortAddress;
}
void otPlatRadioSetAlternateShortAddress(otInstance *aInstance, otShortAddress aShortAddress)
{
    (void)aInstance;
    meuCurtoAlternativo = aShortAddress;
}
otError otPlatRadioGetTransmitPower(otInstance *aInstance, int8_t *aPower)
{
    (void)aInstance;
    if (!aPower) return OT_ERROR_INVALID_ARGS;
    *aPower = potencia;
    return OT_ERROR_NONE;
}
otError otPlatRadioSetTransmitPower(otInstance *aInstance, int8_t aPower)
{
    (void)aInstance;
    potencia = aPower;
    return OT_ERROR_NONE;
}
otError otPlatRadioGetCcaEnergyDetectThreshold(otInstance *aInstance, int8_t *aThreshold)
{
    (void)aInstance;
    *aThreshold = -75;
    return OT_ERROR_NONE;
}
otError otPlatRadioSetCcaEnergyDetectThreshold(otInstance *aInstance, int8_t aThreshold)
{
    (void)aInstance;
    (void)aThreshold;
    return OT_ERROR_NONE;
}
bool otPlatRadioGetPromiscuous(otInstance *aInstance)
{
    (void)aInstance;
    return promiscuo;
}
void otPlatRadioSetPromiscuous(otInstance *aInstance, bool aEnable)
{
    (void)aInstance;
    promiscuo = aEnable;
}
void otPlatRadioSetRxOnWhenIdle(otInstance *aInstance, bool aEnable)
{
    (void)aInstance;
    ligadoNoOcio = aEnable;
}
otRadioState otPlatRadioGetState(otInstance *aInstance)
{
    (void)aInstance;
    return estado;
}
otError otPlatRadioEnable(otInstance *aInstance)
{
    (void)aInstance;
    if (estado == OT_RADIO_STATE_DISABLED) {
        cs_obj_n(nullptr, "__154_ligar", 1);
        quadroTx.mPsdu = pduTx;
        quadroRx.mPsdu = pduRx;
        quadroAck.mPsdu = pduAck;
        estado = OT_RADIO_STATE_SLEEP;
    }
    return OT_ERROR_NONE;
}
otError otPlatRadioDisable(otInstance *aInstance)
{
    (void)aInstance;
    if (estado != OT_RADIO_STATE_DISABLED) {
        cs_obj_n(nullptr, "__154_ligar", 0);
        estado = OT_RADIO_STATE_DISABLED;
    }
    return OT_ERROR_NONE;
}
bool otPlatRadioIsEnabled(otInstance *aInstance)
{
    (void)aInstance;
    return estado != OT_RADIO_STATE_DISABLED;
}
otError otPlatRadioSleep(otInstance *aInstance)
{
    (void)aInstance;
    if (estado == OT_RADIO_STATE_SLEEP || estado == OT_RADIO_STATE_RECEIVE) {
        estado = OT_RADIO_STATE_SLEEP;
        return OT_ERROR_NONE;
    }
    return OT_ERROR_INVALID_STATE;
}
otError otPlatRadioReceive(otInstance *aInstance, uint8_t aChannel)
{
    (void)aInstance;
    if (estado == OT_RADIO_STATE_DISABLED) return OT_ERROR_INVALID_STATE;
    estado = OT_RADIO_STATE_RECEIVE;
    canal = aChannel;
    return OT_ERROR_NONE;
}
otRadioFrame *otPlatRadioGetTransmitBuffer(otInstance *aInstance)
{
    (void)aInstance;
    return &quadroTx;
}
otError otPlatRadioTransmit(otInstance *aInstance, otRadioFrame *aFrame)
{
    (void)aInstance;
    (void)aFrame;
    if (estado != OT_RADIO_STATE_RECEIVE) return OT_ERROR_INVALID_STATE;
    estado = OT_RADIO_STATE_TRANSMIT;
    canal = quadroTx.mChannel;
    enviarAgora = true;
    return OT_ERROR_NONE;
}
int8_t otPlatRadioGetRssi(otInstance *aInstance)
{
    (void)aInstance;
    return RSSI_DA_BANCADA;
}
otError otPlatRadioEnergyScan(otInstance *aInstance, uint8_t aScanChannel, uint16_t aScanDuration)
{
    (void)aInstance;
    (void)aScanChannel;
    varrendo = true;
    fimDaVarredura = millis() + aScanDuration;
    return OT_ERROR_NONE;
}
void otPlatRadioEnableSrcMatch(otInstance *aInstance, bool aEnable)
{
    (void)aInstance;
    comparaOrigem = aEnable;
}
otError otPlatRadioAddSrcMatchShortEntry(otInstance *aInstance, otShortAddress aShortAddress)
{
    (void)aInstance;
    origensCurtas.push_back(aShortAddress);
    return OT_ERROR_NONE;
}
otError otPlatRadioAddSrcMatchExtEntry(otInstance *aInstance, const otExtAddress *aExtAddress)
{
    (void)aInstance;
    origensLongas.push_back(*aExtAddress);
    return OT_ERROR_NONE;
}
otError otPlatRadioClearSrcMatchShortEntry(otInstance *aInstance, otShortAddress aShortAddress)
{
    (void)aInstance;
    for (size_t i = 0; i < origensCurtas.size(); i++) {
        if (origensCurtas[i] == aShortAddress) {
            origensCurtas.erase(origensCurtas.begin() + (long)i);
            return OT_ERROR_NONE;
        }
    }
    return OT_ERROR_NO_ADDRESS;
}
otError otPlatRadioClearSrcMatchExtEntry(otInstance *aInstance, const otExtAddress *aExtAddress)
{
    (void)aInstance;
    for (size_t i = 0; i < origensLongas.size(); i++) {
        if (!memcmp(origensLongas[i].m8, aExtAddress->m8, sizeof(aExtAddress->m8))) {
            origensLongas.erase(origensLongas.begin() + (long)i);
            return OT_ERROR_NONE;
        }
    }
    return OT_ERROR_NO_ADDRESS;
}
void otPlatRadioClearSrcMatchShortEntries(otInstance *aInstance)
{
    (void)aInstance;
    origensCurtas.clear();
}
void otPlatRadioClearSrcMatchExtEntries(otInstance *aInstance)
{
    (void)aInstance;
    origensLongas.clear();
}
uint32_t otPlatRadioGetSupportedChannelMask(otInstance *aInstance)
{
    (void)aInstance;
    return 0x07FFF800; /* os canais 11 a 26 */
}
uint32_t otPlatRadioGetPreferredChannelMask(otInstance *aInstance) { return otPlatRadioGetSupportedChannelMask(aInstance); }

/* ---- os relogios ---- */
void otPlatAlarmMilliStartAt(otInstance *aInstance, uint32_t aT0, uint32_t aDt)
{
    (void)aInstance;
    milliT0 = aT0;
    milliDt = aDt;
    alarmeMilli = true;
}
void otPlatAlarmMilliStop(otInstance *aInstance)
{
    (void)aInstance;
    alarmeMilli = false;
}
uint32_t otPlatAlarmMilliGetNow(void) { return millis(); }
void otPlatAlarmMicroStartAt(otInstance *aInstance, uint32_t aT0, uint32_t aDt)
{
    (void)aInstance;
    microT0 = aT0;
    microDt = aDt;
    alarmeMicro = true;
}
void otPlatAlarmMicroStop(otInstance *aInstance)
{
    (void)aInstance;
    alarmeMicro = false;
}
uint32_t otPlatAlarmMicroGetNow(void) { return micros(); }

/* ---- o resto do chip ---- */
otError otPlatEntropyGet(uint8_t *aOutput, uint16_t aOutputLength)
{
    esp_fill_random(aOutput, aOutputLength);
    return OT_ERROR_NONE;
}
void *otPlatCAlloc(size_t aNum, size_t aSize) { return calloc(aNum, aSize); }
void otPlatFree(void *aPtr) { free(aPtr); }
void otPlatReset(otInstance *aInstance)
{
    (void)aInstance;
    esp_restart();
}
otPlatResetReason otPlatGetResetReason(otInstance *aInstance)
{
    (void)aInstance;
    return OT_PLAT_RESET_REASON_POWER_ON;
}
void otPlatWakeHost(void) {}
void otPlatLog(otLogLevel aLogLevel, otLogRegion aLogRegion, const char *aFormat, ...)
{
    (void)aLogLevel;
    (void)aLogRegion;
    (void)aFormat;
}
void otTaskletsSignalPending(otInstance *aInstance) { (void)aInstance; }

} /* extern "C" */

/* =====================================================================
 * o esp_openthread do IDF */
static otInstance *cs_instancia = nullptr;
static SemaphoreHandle_t cs_trava = nullptr;
static volatile bool cs_sair = false;

extern "C" {

esp_err_t esp_openthread_lock_init(void)
{
    if (!cs_trava) cs_trava = xSemaphoreCreateRecursiveMutex();
    return cs_trava ? ESP_OK : ESP_ERR_NO_MEM;
}
void esp_openthread_lock_deinit(void)
{
    if (cs_trava) vSemaphoreDelete(cs_trava);
    cs_trava = nullptr;
}
bool esp_openthread_lock_acquire(TickType_t block_ticks)
{
    if (!cs_trava) esp_openthread_lock_init();
    return xSemaphoreTakeRecursive(cs_trava, block_ticks) == pdTRUE;
}
void esp_openthread_lock_release(void)
{
    if (cs_trava) xSemaphoreGiveRecursive(cs_trava);
}
bool esp_openthread_task_switching_lock_acquire(TickType_t block_ticks) { return esp_openthread_lock_acquire(block_ticks); }
void esp_openthread_task_switching_lock_release(void) { esp_openthread_lock_release(); }

esp_err_t esp_openthread_init(const esp_openthread_platform_config_t *init_config)
{
    (void)init_config;
    if (cs_instancia) return ESP_ERR_INVALID_STATE;
    esp_openthread_lock_init();
    esp_openthread_lock_acquire(portMAX_DELAY);
    cs_instancia = otInstanceInitSingle();
    esp_openthread_lock_release();
    return cs_instancia ? ESP_OK : ESP_FAIL;
}

otInstance *esp_openthread_get_instance(void) { return cs_instancia; }

esp_err_t esp_openthread_auto_start(otOperationalDatasetTlvs *datasetTlvs)
{
    if (!cs_instancia) return ESP_ERR_INVALID_STATE;
    otOperationalDatasetTlvs dataset;
    if (!datasetTlvs && otDatasetGetActiveTlvs(cs_instancia, &dataset) != OT_ERROR_NONE) return ESP_FAIL;
    if (datasetTlvs && otDatasetSetActiveTlvs(cs_instancia, datasetTlvs) != OT_ERROR_NONE) return ESP_FAIL;
    if (otIp6SetEnabled(cs_instancia, true) != OT_ERROR_NONE) return ESP_FAIL;
    return otThreadSetEnabled(cs_instancia, true) == OT_ERROR_NONE ? ESP_OK : ESP_FAIL;
}

esp_err_t esp_openthread_launch_mainloop(void)
{
    cs_sair = false;
    while (!cs_sair) {
        if (esp_openthread_lock_acquire(portMAX_DELAY)) {
            if (cs_instancia) {
                otTaskletsProcess(cs_instancia);
                processarRadio(cs_instancia);
                processarAlarmes(cs_instancia);
                otTaskletsProcess(cs_instancia);
            }
            esp_openthread_lock_release();
        }
        delay(1);
    }
    return ESP_OK;
}
esp_err_t esp_openthread_mainloop_exit(void)
{
    cs_sair = true;
    return ESP_OK;
}
esp_err_t esp_openthread_deinit(void)
{
    if (!cs_instancia) return ESP_ERR_INVALID_STATE;
    esp_openthread_lock_acquire(portMAX_DELAY);
    otInstanceFinalize(cs_instancia);
    cs_instancia = nullptr;
    alarmeMilli = alarmeMicro = enviarAgora = esperandoAck = varrendo = false;
    esp_openthread_lock_release();
    return ESP_OK;
}

/* A ponte com o lwIP: o OThreadUDP, o CoAP e o DNS usam a pilha direto; os sockets continuam na rede da bancada. */
void *esp_openthread_netif_glue_init(const esp_openthread_platform_config_t *config)
{
    (void)config;
    static int glue;
    return &glue;
}
void esp_openthread_netif_glue_deinit(void) {}
esp_err_t esp_vfs_eventfd_register(const void *config)
{
    (void)config;
    return ESP_OK;
}
esp_err_t esp_vfs_eventfd_unregister(void) { return ESP_OK; }

/* A interface do Thread no esp_netif: os enderecos dela sao os da pilha (otIp6*). */
struct esp_netif_netstack_config {
    int nada;
};
const esp_netif_netstack_config_t g_esp_netif_netstack_default_openthread = {};
const esp_netif_inherent_config_t g_esp_netif_inherent_openthread_config = {};
esp_netif_t *esp_netif_new(const esp_netif_config_t *config)
{
    (void)config;
    static int interfaceDoThread;
    return (esp_netif_t *)&interfaceDoThread;
}
esp_err_t esp_netif_attach(esp_netif_t *esp_netif, esp_netif_iodriver_handle driver_handle)
{
    (void)esp_netif;
    (void)driver_handle;
    return ESP_OK;
}

/* O otPlatDiag (o "diag" da CLI, o de fabrica): o chip real mediria o radio. */
static bool modoDiag = false;
void otPlatDiagSetOutputCallback(otInstance *aInstance, otPlatDiagOutputCallback aCallback, void *aContext)
{
    (void)aInstance;
    (void)aCallback;
    (void)aContext;
}
void otPlatDiagModeSet(bool aMode) { modoDiag = aMode; }
bool otPlatDiagModeGet(void) { return modoDiag; }
void otPlatDiagChannelSet(uint8_t aChannel) { (void)aChannel; }
void otPlatDiagTxPowerSet(int8_t aTxPower) { (void)aTxPower; }
void otPlatDiagRadioReceived(otInstance *aInstance, otRadioFrame *aFrame, otError aError)
{
    (void)aInstance;
    (void)aFrame;
    (void)aError;
}
void otPlatDiagAlarmCallback(otInstance *aInstance) { (void)aInstance; }

void otPlatAssertFail(const char *aFilename, int aLineNumber)
{
    log_e("OpenThread: assert em %s:%d", aFilename, aLineNumber);
    abort();
}

/* A CLI: o que o OThreadCLI digita vai para o interpretador de verdade da pilha. */
esp_err_t esp_openthread_cli_input(const char *line)
{
    if (!line) return ESP_ERR_INVALID_ARG;
    esp_openthread_lock_acquire(portMAX_DELAY);
    otCliInputLine((char *)line);
    esp_openthread_lock_release();
    return ESP_OK;
}

} /* extern "C" */
