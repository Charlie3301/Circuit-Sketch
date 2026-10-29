/* cs_hal.cpp -- o HAL do core da ESP32 (os esp32-hal-*.h de verdade) sobre o
 * simulador.
 *
 * O core oficial tem uma camada em C entre o Arduino e o ESP-IDF: o LEDC
 * com fade, o RMT que manda os bits do LED enderecavel, o DAC, o toque
 * capacitivo, a UART, o SPI e o I2C por numero de barramento, a CPU que
 * muda de frequencia... Aqui cada funcao faz o que a da placa faz, usando o
 * que o simulador ja tem: o PWM do LEDC, as fitas de LED, o Serial, o Wire e
 * o SPI das bibliotecas, as tarefas do FreeRTOS. */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_interno.h"
#include "cs_objeto.h"
#include "Wire.h"
#include "SPI.h"
#include "HardwareSerial.h"
#include "soc/soc_caps.h"
#include "soc/adc_channel.h"
#if SOC_TOUCH_SENSOR_SUPPORTED
#include "soc/touch_sensor_channel.h"
#endif
#include <vector>
#include <map>

/* =====================================================================
 * o log do core (log_e, log_i... e o log_printf) */
extern "C" const char *pathToFileName(const char *path)
{
    const char *p = path;
    for (const char *c = path; *c; c++)
        if (*c == '/' || *c == '\\') p = c + 1;
    return p;
}

extern "C" int log_printf(const char *fmt, ...)
{
    char buf[512];
    va_list a;
    va_start(a, fmt);
    int n = vsnprintf(buf, sizeof(buf), fmt, a);
    va_end(a);
    Serial.print(buf);
    return n;
}

extern "C" void log_print_buf(const uint8_t *b, size_t len)
{
    for (size_t i = 0; i < len; i += 16) {
        log_printf("/* 0x%04X */", (unsigned)i);
        for (size_t k = i; k < i + 16 && k < len; k++) log_printf(" 0x%02X,", b[k]);
        log_printf("\n");
    }
}

/* =====================================================================
 * os pinos (esp32-hal-gpio.h) */
static const int8_t gpioDoAdc1[10] = {
#define CS_C(n) ADC1_CHANNEL_##n##_GPIO_NUM
#ifdef ADC1_CHANNEL_0_GPIO_NUM
    CS_C(0),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_1_GPIO_NUM
    CS_C(1),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_2_GPIO_NUM
    CS_C(2),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_3_GPIO_NUM
    CS_C(3),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_4_GPIO_NUM
    CS_C(4),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_5_GPIO_NUM
    CS_C(5),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_6_GPIO_NUM
    CS_C(6),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_7_GPIO_NUM
    CS_C(7),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_8_GPIO_NUM
    CS_C(8),
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_9_GPIO_NUM
    CS_C(9),
#else
    -1,
#endif
#undef CS_C
};
static const int8_t gpioDoAdc2[10] = {
#define CS_C(n) ADC2_CHANNEL_##n##_GPIO_NUM
#ifdef ADC2_CHANNEL_0_GPIO_NUM
    CS_C(0),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_1_GPIO_NUM
    CS_C(1),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_2_GPIO_NUM
    CS_C(2),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_3_GPIO_NUM
    CS_C(3),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_4_GPIO_NUM
    CS_C(4),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_5_GPIO_NUM
    CS_C(5),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_6_GPIO_NUM
    CS_C(6),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_7_GPIO_NUM
    CS_C(7),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_8_GPIO_NUM
    CS_C(8),
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_9_GPIO_NUM
    CS_C(9),
#else
    -1,
#endif
#undef CS_C
};

/* O canal analogico como o core numera: os do ADC1, e depois os do ADC2
 * somados de SOC_ADC_MAX_CHANNEL_NUM. */
extern "C" int8_t digitalPinToAnalogChannel(uint8_t pin)
{
    for (int i = 0; i < 10; i++)
        if (gpioDoAdc1[i] == (int8_t)pin) return (int8_t)i;
    for (int i = 0; i < 10; i++)
        if (gpioDoAdc2[i] == (int8_t)pin) return (int8_t)(i + SOC_ADC_MAX_CHANNEL_NUM);
    return -1;
}

extern "C" int8_t analogChannelToDigitalPin(uint8_t channel)
{
    if (channel < SOC_ADC_MAX_CHANNEL_NUM) return channel < 10 ? gpioDoAdc1[channel] : -1;
    channel -= SOC_ADC_MAX_CHANNEL_NUM;
    return channel < 10 ? gpioDoAdc2[channel] : -1;
}

#if SOC_TOUCH_SENSOR_SUPPORTED
static int gpioDoToque(int canal)
{
    switch (canal) {
#ifdef TOUCH_PAD_NUM0_GPIO_NUM
    case 0: return TOUCH_PAD_NUM0_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM1_GPIO_NUM
    case 1: return TOUCH_PAD_NUM1_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM2_GPIO_NUM
    case 2: return TOUCH_PAD_NUM2_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM3_GPIO_NUM
    case 3: return TOUCH_PAD_NUM3_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM4_GPIO_NUM
    case 4: return TOUCH_PAD_NUM4_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM5_GPIO_NUM
    case 5: return TOUCH_PAD_NUM5_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM6_GPIO_NUM
    case 6: return TOUCH_PAD_NUM6_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM7_GPIO_NUM
    case 7: return TOUCH_PAD_NUM7_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM8_GPIO_NUM
    case 8: return TOUCH_PAD_NUM8_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM9_GPIO_NUM
    case 9: return TOUCH_PAD_NUM9_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM10_GPIO_NUM
    case 10: return TOUCH_PAD_NUM10_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM11_GPIO_NUM
    case 11: return TOUCH_PAD_NUM11_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM12_GPIO_NUM
    case 12: return TOUCH_PAD_NUM12_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM13_GPIO_NUM
    case 13: return TOUCH_PAD_NUM13_GPIO_NUM;
#endif
#ifdef TOUCH_PAD_NUM14_GPIO_NUM
    case 14: return TOUCH_PAD_NUM14_GPIO_NUM;
#endif
    default: return -1;
    }
}
#endif

extern "C" int8_t digitalPinToTouchChannel(uint8_t pin)
{
#if SOC_TOUCH_SENSOR_SUPPORTED
    for (int c = 0; c < 15; c++)
        if (gpioDoToque(c) == pin) return (int8_t)c;
#endif
    (void)pin;
    return -1;
}

/* O disableInterrupt desliga sem esquecer; o enableInterrupt religa a mesma. */
extern "C" void disableInterrupt(uint8_t pin)
{
    if (pin < CS_PINOS_COM_INTERRUPCAO && cs_interrupcoes[pin].ligada) {
        cs_interrupcoes[pin].ligada = false;
        ch(nullptr, "detachInterrupt", cs_i(pin));
    }
}
extern "C" void enableInterrupt(uint8_t pin)
{
    if (pin < CS_PINOS_COM_INTERRUPCAO && cs_interrupcoes[pin].rotina >= 0 && !cs_interrupcoes[pin].ligada) {
        cs_interrupcoes[pin].ligada = true;
        cs_religar_interrupcao(pin);
    }
}

/* O attachInterrupt com std::function (FunctionalInterrupt.cpp, o do core):
 * a funcao vai com o argumento que a embrulha. */
extern "C" void __attachInterruptFunctionalArg(uint8_t pin, voidFuncPtrArg userFunc, void *arg, int intr_type, bool functional)
{
    (void)functional;
    attachInterruptArg(pin, userFunc, arg, intr_type);
}
#if SOC_GPTIMER_SUPPORTED
extern "C" void __timerAttachInterruptFunctionalArg(hw_timer_t *timer, voidFuncPtrArg userFunc, void *arg, bool functional)
{
    (void)functional;
    timerAttachInterruptArg(timer, userFunc, arg);
}
#endif

/* A matriz de sinais da ESP32 (qualquer periferico em qualquer pino): o
 * simulador liga o periferico direto no pino que a biblioteca pediu. */
extern "C" void pinMatrixOutAttach(uint8_t, uint8_t, bool, bool) {}
extern "C" void pinMatrixOutDetach(uint8_t, bool, bool) {}
extern "C" void pinMatrixInAttach(uint8_t, uint8_t, bool) {}
extern "C" void pinMatrixInDetach(uint8_t, bool, bool) {}

/* =====================================================================
 * o ADC (esp32-hal-adc.h): a leitura com a atenuacao de cada pino.
 *
 * A placa le de 0 ate o fundo de escala da atenuacao (0 dB: ~950 mV;
 * 2,5 dB: ~1250 mV; 6 dB: ~1750 mV; 11 dB, a padrao: ~3100 mV). Abaixo dos
 * 11 dB, uma tensao acima do fundo de escala satura no maximo. */
static uint8_t bitsDoAdc = 12;
static adc_attenuation_t atenuacaoPadrao = ADC_11db;
static std::map<uint8_t, adc_attenuation_t> *atenuacaoDoPino;

static uint32_t fundoDeEscala(adc_attenuation_t a)
{
    switch (a) {
    case ADC_0db: return 950;
    case ADC_2_5db: return 1250;
    case ADC_6db: return 1750;
    default: return 3100;
    }
}
static adc_attenuation_t atenuacaoDe(uint8_t pin)
{
    if (atenuacaoDoPino) {
        auto e = atenuacaoDoPino->find(pin);
        if (e != atenuacaoDoPino->end()) return e->second;
    }
    return atenuacaoPadrao;
}

extern "C" uint16_t analogRead(uint8_t pin)
{
    adc_attenuation_t a = atenuacaoDe(pin);
    if (a == ADC_11db) return (uint16_t)ch_i(nullptr, "analogRead", cs_i(pin));
    uint32_t mv = (uint32_t)ch_i(nullptr, "analogReadMilliVolts", cs_i(pin));
    uint32_t maximo = (1u << bitsDoAdc) - 1;
    uint32_t fe = fundoDeEscala(a);
    return (uint16_t)(mv >= fe ? maximo : (uint64_t)mv * maximo / fe);
}

extern "C" void analogSetAttenuation(adc_attenuation_t attenuation) { atenuacaoPadrao = attenuation; }
extern "C" void analogSetPinAttenuation(uint8_t pin, adc_attenuation_t attenuation)
{
    if (!atenuacaoDoPino) atenuacaoDoPino = new std::map<uint8_t, adc_attenuation_t>();
    (*atenuacaoDoPino)[pin] = attenuation;
}
#if CONFIG_IDF_TARGET_ESP32
extern "C" void analogSetWidth(uint8_t bits)
{
    if (bits < 9) bits = 9;
    if (bits > 12) bits = 12;
    bitsDoAdc = bits;
    analogReadResolution(bits);
}
#endif

/* O modo continuo: uma tarefa le os pinos na frequencia pedida e, a cada
 * "conversions_per_pin" leituras de cada um, guarda a media e chama a funcao. */
struct AdcContinuo {
    std::vector<uint8_t> pinos;
    uint32_t porPino = 1, hz = 20000;
    void (*aviso)(void) = nullptr;
    std::vector<adc_continuous_result_t> resultado, pronto;
    volatile bool rodando = false, novo = false;
    TaskHandle_t tarefa = nullptr;
    SemaphoreHandle_t trava = nullptr;
};
static AdcContinuo *continuo;

static void tarefaDoContinuo(void *)
{
    for (;;) {
        if (!continuo->rodando) {
            vTaskDelay(5);
            continue;
        }
        size_t n = continuo->pinos.size();
        std::vector<uint64_t> somas(n, 0), mv(n, 0);
        for (uint32_t k = 0; k < continuo->porPino; k++)
            for (size_t i = 0; i < n; i++) {
                somas[i] += analogRead(continuo->pinos[i]);
                mv[i] += analogReadMilliVolts(continuo->pinos[i]);
            }
        xSemaphoreTake(continuo->trava, portMAX_DELAY);
        for (size_t i = 0; i < n; i++) {
            continuo->pronto[i].avg_read_raw = (int)(somas[i] / continuo->porPino);
            continuo->pronto[i].avg_read_mvolts = (int)(mv[i] / continuo->porPino);
        }
        continuo->novo = true;
        xSemaphoreGive(continuo->trava);
        if (continuo->aviso) continuo->aviso();
        /* O tempo de juntar as leituras na frequencia pedida. */
        uint64_t us = (uint64_t)continuo->porPino * n * 1000000ULL / (continuo->hz ? continuo->hz : 1);
        vTaskDelay(us >= 1000 ? (TickType_t)(us / 1000) : 1);
    }
}

extern "C" bool analogContinuous(const uint8_t pins[], size_t pins_count, uint32_t conversions_per_pin, uint32_t sampling_freq_hz,
                                 void (*userFunc)(void))
{
    if (!pins || !pins_count || !conversions_per_pin) return false;
    for (size_t i = 0; i < pins_count; i++)
        if (digitalPinToAnalogChannel(pins[i]) < 0 || digitalPinToAnalogChannel(pins[i]) >= SOC_ADC_MAX_CHANNEL_NUM) {
            log_e("Pin %u is not ADC1 pin!", pins[i]);
            return false;
        }
    if (!continuo) {
        continuo = new AdcContinuo();
        continuo->trava = xSemaphoreCreateMutex();
        xTaskCreate(tarefaDoContinuo, "adc_continuous", 4096, nullptr, 5, &continuo->tarefa);
    }
    continuo->rodando = false;
    continuo->pinos.assign(pins, pins + pins_count);
    continuo->porPino = conversions_per_pin;
    continuo->hz = sampling_freq_hz;
    continuo->aviso = userFunc;
    continuo->resultado.assign(pins_count, adc_continuous_result_t{});
    continuo->pronto.assign(pins_count, adc_continuous_result_t{});
    for (size_t i = 0; i < pins_count; i++) {
        continuo->pronto[i].pin = pins[i];
        continuo->pronto[i].channel = (uint8_t)digitalPinToAnalogChannel(pins[i]);
    }
    return true;
}
extern "C" bool analogContinuousRead(adc_continuous_result_t **buffer, uint32_t timeout_ms)
{
    if (!continuo || !buffer) return false;
    uint32_t ate = millis() + timeout_ms;
    while (!continuo->novo) {
        if (millis() >= ate) return false;
        delay(1);
    }
    xSemaphoreTake(continuo->trava, portMAX_DELAY);
    continuo->resultado = continuo->pronto;
    continuo->novo = false;
    xSemaphoreGive(continuo->trava);
    *buffer = continuo->resultado.data();
    return true;
}
extern "C" bool analogContinuousStart()
{
    if (!continuo) return false;
    continuo->rodando = true;
    return true;
}
extern "C" bool analogContinuousStop()
{
    if (!continuo) return false;
    continuo->rodando = false;
    return true;
}
extern "C" bool analogContinuousDeinit()
{
    if (!continuo) return false;
    continuo->rodando = false;
    continuo->pinos.clear();
    return true;
}
extern "C" void analogContinuousSetAtten(adc_attenuation_t attenuation) { analogSetAttenuation(attenuation); }
extern "C" void analogContinuousSetWidth(uint8_t bits) { bitsDoAdc = bits; }

/* =====================================================================
 * o DAC (esp32-hal-dac.h): a tensao de saida (0 a 255 = 0 a 3,3 V) no pino. */
#if SOC_DAC_SUPPORTED
#include "soc/dac_channel.h"
extern "C" bool dacWrite(uint8_t pin, uint8_t value)
{
    if (pin != DAC_CHAN0_GPIO_NUM && pin != DAC_CHAN1_GPIO_NUM) {
        log_e("pin %u is not a DAC pin", pin);
        return false;
    }
    /* A saida do DAC e uma tensao fixa: para quem le a media (o LED, o
     * multimetro), e o mesmo que o PWM rapido com esse ciclo. */
    ch(nullptr, "ledcAttach", cs_i(pin), cs_i(1000000), cs_i(8));
    return ch_i(nullptr, "ledcWrite", cs_i(pin), cs_i(value)) != 0;
}
extern "C" bool dacDisable(uint8_t pin)
{
    if (pin != DAC_CHAN0_GPIO_NUM && pin != DAC_CHAN1_GPIO_NUM) return false;
    ch(nullptr, "ledcWrite", cs_i(pin), cs_i(0));
    ch(nullptr, "ledcDetach", cs_i(pin));
    return true;
}
#endif

/* =====================================================================
 * o sigma-delta (esp32-hal-sigmadelta.h): a media da saida e o ciclo pedido. */
#if SOC_SDM_SUPPORTED
extern "C" bool sigmaDeltaAttach(uint8_t pin, uint32_t freq)
{
    return ch_i(nullptr, "ledcAttach", cs_i(pin), cs_i(freq ? freq : 312500), cs_i(8)) != 0;
}
extern "C" bool sigmaDeltaWrite(uint8_t pin, uint8_t duty) { return ch_i(nullptr, "ledcWrite", cs_i(pin), cs_i(duty)) != 0; }
extern "C" bool sigmaDeltaDetach(uint8_t pin)
{
    ch(nullptr, "ledcWrite", cs_i(pin), cs_i(0));
    return ch_i(nullptr, "ledcDetach", cs_i(pin)) != 0;
}
#endif

/* =====================================================================
 * o LEDC alem do basico (esp32-hal-ledc.h): o canal, a nota, a inversao, o
 * fade (com a curva gama) e a fonte do relogio. */
struct EstadoDoLedc {
    esp_timer_handle_t fade = nullptr;
    uint32_t de = 0, para = 0, passos = 0, passo = 0;
    bool gama = false;
    void (*fim)(void *) = nullptr;
    void *arg = nullptr;
};
static std::map<uint8_t, EstadoDoLedc> *ledcs;
static ledc_clk_cfg_t relogioDoLedc = LEDC_AUTO_CLK;
static float fatorGama = 2.8f;
static std::vector<float> *tabelaGama;

static EstadoDoLedc &ledcDe(uint8_t pin)
{
    if (!ledcs) ledcs = new std::map<uint8_t, EstadoDoLedc>();
    return (*ledcs)[pin];
}

extern "C" ledc_clk_cfg_t ledcGetClockSource(void) { return relogioDoLedc; }
extern "C" bool ledcSetClockSource(ledc_clk_cfg_t source)
{
    relogioDoLedc = source;
    return true;
}

extern "C" bool ledcWriteChannel(uint8_t channel, uint32_t duty) { return ch_i(nullptr, "ledcWrite", cs_i(channel), cs_i(duty)) != 0; }

/* As notas da oitava 8 (o core oficial divide pela oitava pedida). */
static const uint16_t notaBase[NOTE_MAX] = {4186, 4435, 4699, 4978, 5274, 5588, 5920, 6272, 6645, 7040, 7459, 7902};

extern "C" uint32_t ledcWriteNote(uint8_t pin, note_t note, uint8_t octave)
{
    if (octave > 8 || note >= NOTE_MAX) return 0;
    uint32_t f = (uint32_t)(notaBase[note] / (double)(1 << (8 - octave)) + 0.5);
    return ledcWriteTone(pin, f);
}

extern "C" uint32_t ledcReadFreq(uint8_t pin) { return pin < CS_PINOS_COM_INTERRUPCAO ? cs_ledc[pin].freq : 0; }

extern "C" bool ledcOutputInvert(uint8_t pin, bool out_invert)
{
    if (pin >= CS_PINOS_COM_INTERRUPCAO) return false;
    cs_ledc[pin].invertido = out_invert;
    return true;
}

#ifdef SOC_LEDC_GAMMA_CURVE_FADE_SUPPORTED
extern "C" bool ledcSetGammaTable(const float *gamma_table, uint16_t size)
{
    if (!gamma_table || !size) return false;
    if (!tabelaGama) tabelaGama = new std::vector<float>();
    tabelaGama->assign(gamma_table, gamma_table + size);
    return true;
}
extern "C" void ledcClearGammaTable(void)
{
    if (tabelaGama) tabelaGama->clear();
}
extern "C" void ledcSetGammaFactor(float factor) { fatorGama = factor; }
#endif

static uint32_t comGama(uint32_t duty, uint32_t maximo)
{
    if (!maximo) return duty;
    float x = (float)duty / maximo;
    if (tabelaGama && !tabelaGama->empty()) {
        size_t i = (size_t)(x * (tabelaGama->size() - 1) + 0.5f);
        return (uint32_t)(tabelaGama->at(i) * maximo + 0.5f);
    }
    return (uint32_t)(powf(x, fatorGama) * maximo + 0.5f);
}

/* O passo do fade: a cada 10 ms, o ciclo anda em linha reta (ou na curva
 * gama) do inicial ao final; no fim, a funcao do sketch (a "interrupcao"). */
static void passoDoFade(void *arg)
{
    uint8_t pin = (uint8_t)(uintptr_t)arg;
    EstadoDoLedc &e = ledcDe(pin);
    e.passo++;
    uint32_t maximo = (1u << (pin < CS_PINOS_COM_INTERRUPCAO && cs_ledc[pin].bits ? cs_ledc[pin].bits : 8)) - 1;
    int64_t d = (int64_t)e.de + ((int64_t)e.para - (int64_t)e.de) * (int64_t)e.passo / (int64_t)(e.passos ? e.passos : 1);
    uint32_t duty = (uint32_t)d;
    if (e.gama) duty = comGama(duty, maximo);
    ledcWrite(pin, duty);
    if (e.passo >= e.passos) {
        esp_timer_stop(e.fade);
        if (e.fim) e.fim(e.arg);
    }
}

static bool comecarFade(uint8_t pin, uint32_t start, uint32_t target, int ms, bool gama, void (*fim)(void *), void *arg)
{
    EstadoDoLedc &e = ledcDe(pin);
    if (!e.fade) {
        esp_timer_create_args_t a = {};
        a.callback = passoDoFade;
        a.arg = (void *)(uintptr_t)pin;
        a.name = "ledc_fade";
        if (esp_timer_create(&a, &e.fade) != ESP_OK) return false;
    }
    esp_timer_stop(e.fade);
    e.de = start;
    e.para = target;
    e.passo = 0;
    e.passos = ms > 10 ? (uint32_t)(ms / 10) : 1;
    e.gama = gama;
    e.fim = fim;
    e.arg = arg;
    ledcWrite(pin, start);
    return esp_timer_start_periodic(e.fade, 10000) == ESP_OK;
}

static void chamarSemArgumento(void *f) { ((void (*)(void))f)(); }

extern "C" bool ledcFade(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms)
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, false, nullptr, nullptr);
}
extern "C" bool ledcFadeWithInterrupt(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms, void (*userFunc)(void))
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, false, chamarSemArgumento, (void *)userFunc);
}
extern "C" bool ledcFadeWithInterruptArg(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms,
                                         void (*userFunc)(void *), void *arg)
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, false, userFunc, arg);
}
#ifdef SOC_LEDC_GAMMA_CURVE_FADE_SUPPORTED
extern "C" bool ledcFadeGamma(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms)
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, true, nullptr, nullptr);
}
extern "C" bool ledcFadeGammaWithInterrupt(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms,
                                           void (*userFunc)(void))
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, true, chamarSemArgumento, (void *)userFunc);
}
extern "C" bool ledcFadeGammaWithInterruptArg(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms,
                                              void (*userFunc)(void *), void *arg)
{
    return comecarFade(pin, start_duty, target_duty, max_fade_time_ms, true, userFunc, arg);
}
#endif

/* =====================================================================
 * o LED RGB na ordem pedida (esp32-hal-rgb-led.h): a ordem e a dos fios do
 * LED de verdade; o que ele mostra e a cor pedida. */
extern "C" void rgbLedWriteOrdered(uint8_t pin, rgb_led_color_order_t order, uint8_t red_val, uint8_t green_val, uint8_t blue_val)
{
    (void)order;
    rgbLedWrite(pin, red_val, green_val, blue_val);
}

/* =====================================================================
 * o toque capacitivo (esp32-hal-touch.h e -touch-ng.h).
 *
 * O valor e o que o sensor do chip da: na ESP32 (versao 1), ~75 sem dedo e
 * cai quando encosta; na S2/S3 (versao 2) e na P4, ~25000 e sobe. Quanto o
 * dedo encosta vem da bancada (a leitura "pino:toque"). */
#if SOC_TOUCH_SENSOR_SUPPORTED
struct EstadoDoToque {
    void (*fn)(void *) = nullptr;
    void *arg = nullptr;
    touch_value_t limiar = 0;
    bool ultimo = false;
};
static std::map<uint8_t, EstadoDoToque> *toques;
static bool abaixoDoLimiar = true;
static esp_timer_handle_t vigiaDoToque;

extern "C" touch_value_t touchRead(uint8_t pin)
{
    if (digitalPinToTouchChannel(pin) < 0) {
        log_e(" No touch pad on selected pin %u!", pin);
        return 0;
    }
    double t = ch_r(nullptr, "__toque", cs_i(pin));
#if SOC_TOUCH_SENSOR_VERSION == 1
    return (touch_value_t)(75 - 63 * t + 0.5);
#else
    return (touch_value_t)(25000 + 50000 * t + 0.5);
#endif
}

static bool tocado(uint8_t pin, const EstadoDoToque &e)
{
    touch_value_t v = touchRead(pin);
#if SOC_TOUCH_SENSOR_VERSION == 1
    return abaixoDoLimiar ? v < e.limiar : v > e.limiar;
#else
    return v > e.limiar;
#endif
}

/* A interrupcao do toque: o sensor e lido a cada 10 ms, como o FSM do chip. */
static void vigiarToques(void *)
{
    if (!toques) return;
    for (auto &[pin, e] : *toques) {
        if (!e.fn) continue;
        bool agora = tocado(pin, e);
#if SOC_TOUCH_SENSOR_VERSION == 1
        if (agora) e.fn(e.arg);
#else
        if (agora != e.ultimo) e.fn(e.arg);
#endif
        e.ultimo = agora;
    }
}

static void ligarToque(uint8_t pin, void (*fn)(void *), void *arg, touch_value_t limiar)
{
    if (!toques) toques = new std::map<uint8_t, EstadoDoToque>();
    EstadoDoToque &e = (*toques)[pin];
    e.fn = fn;
    e.arg = arg;
    e.limiar = limiar;
    e.ultimo = false;
    if (!vigiaDoToque) {
        esp_timer_create_args_t a = {};
        a.callback = vigiarToques;
        a.name = "touch";
        esp_timer_create(&a, &vigiaDoToque);
        esp_timer_start_periodic(vigiaDoToque, 10000);
    }
}

extern "C" void touchAttachInterrupt(uint8_t pin, void (*userFunc)(void), touch_value_t threshold)
{
    ligarToque(pin, chamarSemArgumento, (void *)userFunc, threshold);
}
extern "C" void touchAttachInterruptArg(uint8_t pin, void (*userFunc)(void *), void *arg, touch_value_t threshold)
{
    ligarToque(pin, userFunc, arg, threshold);
}
extern "C" void touchDetachInterrupt(uint8_t pin)
{
    if (toques) toques->erase(pin);
}
extern "C" bool touchInterruptGetLastStatus(uint8_t pin)
{
    if (!toques || !toques->count(pin)) return false;
    return tocado(pin, (*toques)[pin]);
}
extern "C" void touchInterruptSetThresholdDirection(bool mustbeLower) { abaixoDoLimiar = mustbeLower; }
extern "C" void touchSleepWakeUpEnable(uint8_t pin, touch_value_t threshold)
{
    (void)threshold;
    (void)pin;
    esp_sleep_enable_touchpad_wakeup();
}
extern "C" void touchSetCycles(uint16_t measure, uint16_t sleep)
{
    (void)measure;
    (void)sleep;
}
extern "C" void touchSetTiming(float measure, uint32_t sleep)
{
    (void)measure;
    (void)sleep;
}
extern "C" void touchSetDefaultThreshold(float percentage) { (void)percentage; }
#if SOC_TOUCH_SENSOR_VERSION == 1
extern "C" void touchSetConfig(float duration_ms, touch_volt_lim_l_t volt_low, touch_volt_lim_h_t volt_high)
{
    (void)duration_ms;
    (void)volt_low;
    (void)volt_high;
}
#elif SOC_TOUCH_SENSOR_VERSION == 2
extern "C" void touchSetConfig(uint32_t chg_times, touch_volt_lim_l_t volt_low, touch_volt_lim_h_t volt_high)
{
    (void)chg_times;
    (void)volt_low;
    (void)volt_high;
}
#endif
#endif

/* =====================================================================
 * o RMT (esp32-hal-rmt.h): os pulsos com tempo exato.
 *
 * O uso de quase todo sketch e o LED enderecavel (WS2812): cada bit e um
 * pulso alto curto (0) ou longo (1) de ~1,25 us. O que tem esse jeito vira
 * a fita de LEDs do pino; o resto (o controle remoto, um protocolo proprio)
 * sai no pino, nivel a nivel, com o tempo de cada simbolo. */
#if SOC_RMT_SUPPORTED
struct EstadoDoRmt {
    rmt_ch_dir_t direcao = RMT_TX_MODE;
    uint32_t hz = 10000000;
    uint8_t fim = 0;
    bool portadora = false;
    bool pronto = true;
    uint32_t ocioso = 0xFFFF;
};
static std::map<int, EstadoDoRmt> *rmts;

static EstadoDoRmt *rmtDe(int pin)
{
    if (!rmts) return nullptr;
    auto e = rmts->find(pin);
    return e == rmts->end() ? nullptr : &e->second;
}

extern "C" bool rmtInit(int pin, rmt_ch_dir_t channel_direction, rmt_reserve_memsize_t memsize, uint32_t frequency_Hz)
{
    (void)memsize;
    if (frequency_Hz < 312500 || frequency_Hz > 80000000) {
        log_e("GPIO %d - Bad RMT frequency resolution. Must be between 312.5KHz to 80MHz.", pin);
        return false;
    }
    if (!rmts) rmts = new std::map<int, EstadoDoRmt>();
    EstadoDoRmt &e = (*rmts)[pin];
    e.direcao = channel_direction;
    e.hz = frequency_Hz;
    pinMode(pin, channel_direction == RMT_TX_MODE ? OUTPUT : INPUT);
    return true;
}
extern "C" bool rmtDeinit(int pin)
{
    if (rmts) rmts->erase(pin);
    return true;
}
extern "C" bool rmtSetEOT(int pin, uint8_t EOT_Level)
{
    EstadoDoRmt *e = rmtDe(pin);
    if (!e) return false;
    e->fim = EOT_Level;
    return true;
}
extern "C" bool rmtSetCarrier(int pin, bool carrier_en, bool carrier_level, uint32_t frequency_Hz, float duty_percent)
{
    (void)carrier_level;
    (void)frequency_Hz;
    (void)duty_percent;
    EstadoDoRmt *e = rmtDe(pin);
    if (!e) return false;
    e->portadora = carrier_en;
    return true;
}
extern "C" bool rmtSetRxMinThreshold(int pin, uint8_t filter_pulse_ticks)
{
    (void)filter_pulse_ticks;
    return rmtDe(pin) != nullptr;
}
extern "C" bool rmtSetRxMaxThreshold(int pin, uint16_t idle_thres_ticks)
{
    EstadoDoRmt *e = rmtDe(pin);
    if (!e) return false;
    e->ocioso = idle_thres_ticks;
    return true;
}

/* Os simbolos tem o jeito do WS2812? Devolve os bytes (G R B por LED). */
static bool comoLedEnderecavel(const EstadoDoRmt &e, const rmt_data_t *d, size_t n, std::vector<uint8_t> &bytes)
{
    double ns = 1e9 / e.hz;
    size_t bits = 0;
    uint8_t atual = 0;
    for (size_t i = 0; i < n; i++) {
        double alto = d[i].level0 ? d[i].duration0 * ns : d[i].duration1 * ns;
        double total = (d[i].duration0 + d[i].duration1) * ns;
        if (d[i].level0 == d[i].level1) {
            /* O reset (ou o fim) da fita: um nivel baixo longo. */
            if (!d[i].level0 && i == n - 1) break;
            return false;
        }
        if (total < 900 || total > 2600 || alto < 150 || alto > 1700) return false;
        atual = (uint8_t)((atual << 1) | (alto > total * 0.45 ? 1 : 0));
        if (++bits % 8 == 0) {
            bytes.push_back(atual);
            atual = 0;
        }
    }
    return bits >= 24 && bits % 24 == 0;
}

static bool escreverRmt(int pin, const rmt_data_t *data, size_t n)
{
    EstadoDoRmt *e = rmtDe(pin);
    if (!e || e->direcao != RMT_TX_MODE || !data) return false;
    std::vector<uint8_t> bytes;
    if (comoLedEnderecavel(*e, data, n, bytes)) {
        cs_obj_n(nullptr, "__fita", pin, cs_vetor(bytes.data(), (int)bytes.size()));
        delayMicroseconds((uint32_t)(n * 1.25) + 50);
        return true;
    }
    double us = 1e6 / e->hz;
    for (size_t i = 0; i < n; i++) {
        digitalWrite(pin, data[i].level0);
        if (data[i].duration0) delayMicroseconds((uint32_t)(data[i].duration0 * us));
        digitalWrite(pin, data[i].level1);
        if (data[i].duration1) delayMicroseconds((uint32_t)(data[i].duration1 * us));
        if (!data[i].duration0 || !data[i].duration1) break;
    }
    digitalWrite(pin, e->fim);
    return true;
}

extern "C" bool rmtWrite(int pin, rmt_data_t *data, size_t num_rmt_symbols, uint32_t timeout_ms)
{
    (void)timeout_ms;
    return escreverRmt(pin, data, num_rmt_symbols);
}
extern "C" bool rmtWriteAsync(int pin, rmt_data_t *data, size_t num_rmt_symbols) { return escreverRmt(pin, data, num_rmt_symbols); }
extern "C" bool rmtWriteLooping(int pin, rmt_data_t *data, size_t num_rmt_symbols) { return escreverRmt(pin, data, num_rmt_symbols); }
extern "C" bool rmtWriteRepeated(int pin, rmt_data_t *data, size_t num_rmt_symbols, uint32_t loop_count)
{
    bool ok = true;
    for (uint32_t k = 0; k < (loop_count ? loop_count : 1) && ok; k++) ok = escreverRmt(pin, data, num_rmt_symbols);
    return ok;
}
extern "C" bool rmtTransmitCompleted(int pin) { return rmtDe(pin) != nullptr; }

/* A recepcao: as bordas do pino, medidas no relogio, ate o tempo ocioso. */
extern "C" bool rmtRead(int pin, rmt_data_t *data, size_t *num_rmt_symbols, uint32_t timeout_ms)
{
    EstadoDoRmt *e = rmtDe(pin);
    if (!e || e->direcao != RMT_RX_MODE || !data || !num_rmt_symbols) return false;
    double ticksPorUs = e->hz / 1e6;
    uint32_t ociosoUs = (uint32_t)(e->ocioso / ticksPorUs);
    unsigned long inicio = millis();
    int nivel = digitalRead(pin);
    while (digitalRead(pin) == nivel) {
        if (millis() - inicio > timeout_ms) {
            *num_rmt_symbols = 0;
            return false;
        }
        delayMicroseconds(20);
    }
    size_t max = *num_rmt_symbols, n = 0;
    int meia = 0;
    nivel = digitalRead(pin);
    uint32_t desde = micros();
    while (n < max) {
        uint32_t agora = micros();
        int v = digitalRead(pin);
        bool parado = agora - desde > ociosoUs;
        if (v != nivel || parado) {
            uint32_t ticks = (uint32_t)((agora - desde) * ticksPorUs);
            if (ticks > 0x7FFF) ticks = 0x7FFF;
            if (meia == 0) {
                data[n].level0 = (uint32_t)nivel;
                data[n].duration0 = ticks;
                meia = 1;
            } else {
                data[n].level1 = (uint32_t)nivel;
                data[n].duration1 = ticks;
                meia = 0;
                n++;
            }
            if (parado) break;
            nivel = v;
            desde = agora;
        }
        delayMicroseconds(5);
    }
    if (meia && n < max) {
        data[n].level1 = 0;
        data[n].duration1 = 0;
        n++;
    }
    *num_rmt_symbols = n;
    return n > 0;
}
extern "C" bool rmtReadAsync(int pin, rmt_data_t *data, size_t *num_rmt_symbols) { return rmtRead(pin, data, num_rmt_symbols, 0); }
extern "C" bool rmtReceiveCompleted(int pin) { return rmtDe(pin) != nullptr; }
#endif

/* =====================================================================
 * a CPU (esp32-hal-cpu.h): a frequencia que o sketch escolhe. */
static uint32_t cpuMhz = CONFIG_ESP_DEFAULT_CPU_FREQ_MHZ;
struct AvisoDoApb {
    void *arg;
    apb_change_cb_t cb;
};
static std::vector<AvisoDoApb> *avisosDoApb;

static uint32_t apbDe(uint32_t mhz) { return mhz >= 80 ? 80000000 : mhz * 1000000; }

extern "C" bool addApbChangeCallback(void *arg, apb_change_cb_t cb)
{
    if (!avisosDoApb) avisosDoApb = new std::vector<AvisoDoApb>();
    avisosDoApb->push_back({arg, cb});
    return true;
}
extern "C" bool removeApbChangeCallback(void *arg, apb_change_cb_t cb)
{
    if (!avisosDoApb) return false;
    for (size_t i = 0; i < avisosDoApb->size(); i++)
        if ((*avisosDoApb)[i].arg == arg && (*avisosDoApb)[i].cb == cb) {
            avisosDoApb->erase(avisosDoApb->begin() + i);
            return true;
        }
    return false;
}

extern "C" const char *getSupportedCpuFrequencyMhz(void)
{
#if CONFIG_IDF_TARGET_ESP32C3 || CONFIG_IDF_TARGET_ESP32C6
    return "160, 80, 40, 20, 10";
#elif CONFIG_IDF_TARGET_ESP32H2
    return "96, 64, 48, 32, 16";
#else
    return "240, 160, 80, 40, 20, 10";
#endif
}

extern "C" bool setCpuFrequencyMhz(uint32_t cpu_freq_mhz)
{
    char lista[48];
    snprintf(lista, sizeof(lista), "%s,", getSupportedCpuFrequencyMhz());
    char procurado[8];
    snprintf(procurado, sizeof(procurado), "%u,", (unsigned)cpu_freq_mhz);
    bool vale = strncmp(lista, procurado, strlen(procurado)) == 0;
    for (char *c = lista; *c && !vale; c++)
        if (*c == ' ' && strncmp(c + 1, procurado, strlen(procurado)) == 0) vale = true;
    if (!vale) {
        log_e("Bad frequency: %u MHz! Options are: %s MHz", (unsigned)cpu_freq_mhz, getSupportedCpuFrequencyMhz());
        return false;
    }
    if (cpu_freq_mhz == cpuMhz) return true;
    uint32_t antes = apbDe(cpuMhz), depois = apbDe(cpu_freq_mhz);
    if (avisosDoApb && antes != depois)
        for (auto &a : *avisosDoApb) a.cb(a.arg, APB_BEFORE_CHANGE, antes, depois);
    cpuMhz = cpu_freq_mhz;
    if (avisosDoApb && antes != depois)
        for (auto &a : *avisosDoApb) a.cb(a.arg, APB_AFTER_CHANGE, antes, depois);
    return true;
}
extern "C" uint32_t getCpuFrequencyMhz() { return cpuMhz; }
extern "C" uint32_t getXtalFrequencyMhz()
{
#if CONFIG_IDF_TARGET_ESP32H2
    return 32;
#else
    return 40;
#endif
}
extern "C" uint32_t getApbFrequency() { return apbDe(cpuMhz); }
extern "C" const char *getClockSourceName(uint8_t source)
{
    switch (source) {
    case 0: return "XTAL";
    case 1: return "PLL";
    case 2: return "RC_FAST";
    case 3: return "APLL";
    default: return "Invalid";
    }
}

/* =====================================================================
 * o Bluetooth (esp32-hal-bt.h): o controlador ligado ou nao. */
#if SOC_BT_SUPPORTED
static bool btLigado, btLiberado;
extern "C" bool _btInUse_default(void) { return false; }
/* As bibliotecas de Bluetooth trocam esta (fraca) pela delas. */
extern "C" __attribute__((weak)) bool btInUse(void) { return btLigado; }
extern "C" bool btClassicInUse(void) { return btLigado; }
extern "C" bool bleInUse(void) { return btLigado; }
extern "C" bool btStarted() { return btLigado; }
extern "C" bool btStartMode(bt_mode mode)
{
    (void)mode;
    if (btLiberado) {
        log_e("BT memory was released: it can't be started again");
        return false;
    }
    btLigado = true;
    return true;
}
extern "C" bool btStart() { return btStartMode(BT_MODE_DEFAULT); }
extern "C" bool btStop()
{
    btLigado = false;
    return true;
}
extern "C" bool btMemRelease(bt_mode mode)
{
    (void)mode;
    btLiberado = true;
    return true;
}
extern "C" bool btMemReleased(bt_mode mode)
{
    (void)mode;
    return btLiberado;
}
extern "C" void btMarkMemReleased(bt_mode mode)
{
    (void)mode;
    btLiberado = true;
}
#endif

/* =====================================================================
 * a PSRAM no heap (esp32-hal-psram.h) */
extern "C" bool psramAddToHeap() { return psramFound(); }

/* =====================================================================
 * o resto do esp32-hal.h: os watchdogs, a tarefa em qualquer nucleo, o
 * tratador de panico. */
extern "C" void enableLoopWDT() { ch(nullptr, "enableLoopWDT"); }
extern "C" void disableLoopWDT() { ch(nullptr, "disableLoopWDT"); }
extern "C" void feedLoopWDT() { ch(nullptr, "feedLoopWDT"); }
extern "C" void enableCore0WDT() { ch(nullptr, "enableCore0WDT"); }
extern "C" bool disableCore0WDT() { return ch_i(nullptr, "disableCore0WDT") != 0 || true; }
#ifndef CONFIG_FREERTOS_UNICORE
extern "C" void enableCore1WDT() { ch(nullptr, "enableCore1WDT"); }
extern "C" bool disableCore1WDT() { return ch_i(nullptr, "disableCore1WDT") != 0 || true; }
#endif

extern "C" BaseType_t xTaskCreateUniversal(TaskFunction_t pxTaskCode, const char *const pcName, const uint32_t usStackDepth,
                                           void *const pvParameters, UBaseType_t uxPriority, TaskHandle_t *const pxCreatedTask,
                                           const BaseType_t xCoreID)
{
#ifndef CONFIG_FREERTOS_UNICORE
    if (xCoreID >= 0 && xCoreID < 2)
        return xTaskCreatePinnedToCore(pxTaskCode, pcName, usStackDepth, pvParameters, uxPriority, pxCreatedTask, xCoreID);
#endif
    (void)xCoreID;
    return xTaskCreate(pxTaskCode, pcName, usStackDepth, pvParameters, uxPriority, pxCreatedTask);
}

extern "C" void vPortYield(void) { yield(); }
extern "C" bool testSPIRAM(void) { return psramFound(); }
extern "C" void arduino_phy_init() {}
extern "C" void initArduino() {}

static arduino_panic_handler_t tratadorDePanico;
static void *argDoPanico;
extern "C" void set_arduino_panic_handler(arduino_panic_handler_t handler, void *arg)
{
    tratadorDePanico = handler;
    argDoPanico = arg;
}
extern "C" arduino_panic_handler_t get_arduino_panic_handler(void) { return tratadorDePanico; }
extern "C" void *get_arduino_panic_handler_arg(void) { return argDoPanico; }

/* =====================================================================
 * a UART por numero (esp32-hal-uart.h): o Serial, o Serial1 e o Serial2. */
struct uart_struct_t {
    uint8_t num;
    uint32_t baud;
    int8_t rx, tx, cts, rts;
    bool instalada;
};
static uart_struct_t uarts[SOC_UART_HP_NUM > 3 ? SOC_UART_HP_NUM : 3] = {};
static int depuracaoDaUart = -1;

static HardwareSerial &serialDe(uart_t *u)
{
    if (u && u->num == 1) return Serial1;
    if (u && u->num == 2) return Serial2;
    return Serial;
}

extern "C" uart_t *uartBegin(uint8_t uart_nr, uint32_t baudrate, uint32_t config, int8_t rxPin, int8_t txPin, uint32_t rx_buffer_size,
                             uint32_t tx_buffer_size, bool inverted, uint8_t rxfifo_full_thrhd)
{
    (void)rx_buffer_size;
    (void)tx_buffer_size;
    (void)rxfifo_full_thrhd;
    if (uart_nr >= sizeof(uarts) / sizeof(uarts[0])) return nullptr;
    uart_t *u = &uarts[uart_nr];
    u->num = uart_nr;
    u->baud = baudrate;
    u->rx = rxPin;
    u->tx = txPin;
    u->instalada = true;
    serialDe(u).begin(baudrate, config, rxPin, txPin, inverted);
    return u;
}
extern "C" bool _testUartBegin(uint8_t uart_nr, uint32_t baudrate, uint32_t config, int8_t rxPin, int8_t txPin, uint32_t rx_buffer_size,
                               uint32_t tx_buffer_size, bool inverted, uint8_t rxfifo_full_thrhd)
{
    return uartBegin(uart_nr, baudrate, config, rxPin, txPin, rx_buffer_size, tx_buffer_size, inverted, rxfifo_full_thrhd) != nullptr;
}
extern "C" void uartEnd(uint8_t uart_num)
{
    if (uart_num >= sizeof(uarts) / sizeof(uarts[0])) return;
    serialDe(&uarts[uart_num]).end();
    uarts[uart_num].instalada = false;
}
extern "C" void uartGetEventQueue(uart_t *uart, QueueHandle_t *q)
{
    (void)uart;
    if (q) *q = nullptr;
}
extern "C" uint32_t uartAvailable(uart_t *uart) { return (uint32_t)serialDe(uart).available(); }
extern "C" uint32_t uartAvailableForWrite(uart_t *uart) { return (uint32_t)serialDe(uart).availableForWrite(); }
extern "C" size_t uartReadBytes(uart_t *uart, uint8_t *buffer, size_t size, uint32_t timeout_ms)
{
    size_t n = 0;
    uint32_t ate = millis() + timeout_ms;
    while (n < size) {
        int c = serialDe(uart).read();
        if (c >= 0) {
            buffer[n++] = (uint8_t)c;
            continue;
        }
        if (millis() >= ate) break;
        delay(1);
    }
    return n;
}
extern "C" uint8_t uartRead(uart_t *uart)
{
    int c = serialDe(uart).read();
    return c < 0 ? 0 : (uint8_t)c;
}
extern "C" uint8_t uartPeek(uart_t *uart)
{
    int c = serialDe(uart).peek();
    return c < 0 ? 0 : (uint8_t)c;
}
extern "C" void uartWrite(uart_t *uart, uint8_t c) { serialDe(uart).write(c); }
extern "C" void uartWriteBuf(uart_t *uart, const uint8_t *data, size_t len) { serialDe(uart).write(data, len); }
extern "C" void uartFlush(uart_t *uart) { serialDe(uart).flush(); }
extern "C" void uartFlushTxOnly(uart_t *uart, bool txOnly)
{
    (void)txOnly;
    serialDe(uart).flush();
}
extern "C" bool uartSetBaudRate(uart_t *uart, uint32_t baud_rate)
{
    if (!uart) return false;
    uart->baud = baud_rate;
    serialDe(uart).updateBaudRate(baud_rate);
    return true;
}
extern "C" uint32_t uartGetBaudRate(uart_t *uart) { return uart ? uart->baud : 0; }
extern "C" bool uartPinSignalInversion(uart_t *uart, uint32_t invMask, bool inverted) { return uart && (invMask || !inverted || true); }
extern "C" bool uartSetRxInvert(uart_t *uart, bool invert) { return uart && (invert || true); }
extern "C" bool uartSetTxInvert(uart_t *uart, bool invert) { return uart && (invert || true); }
extern "C" bool uartSetCtsInvert(uart_t *uart, bool invert) { return uart && (invert || true); }
extern "C" bool uartSetRtsInvert(uart_t *uart, bool invert) { return uart && (invert || true); }
extern "C" bool uartSetRxTimeout(uart_t *uart, uint8_t numSymbTimeout) { return uart && (numSymbTimeout || true); }
extern "C" bool uartSetRxFIFOFull(uart_t *uart, uint8_t numBytesFIFOFull) { return uart && (numBytesFIFOFull || true); }
extern "C" void uartSetFastReading(uart_t *uart) { (void)uart; }
extern "C" void uartSetDebug(uart_t *uart) { depuracaoDaUart = uart ? uart->num : -1; }
extern "C" int uartGetDebug() { return depuracaoDaUart; }
extern "C" bool uartIsDriverInstalled(uart_t *uart) { return uart && uart->instalada; }
extern "C" bool uartSetPins(uint8_t uart_num, int8_t rxPin, int8_t txPin, int8_t ctsPin, int8_t rtsPin)
{
    if (uart_num >= sizeof(uarts) / sizeof(uarts[0])) return false;
    uart_t *u = &uarts[uart_num];
    u->num = uart_num;
    if (rxPin >= 0) u->rx = rxPin;
    if (txPin >= 0) u->tx = txPin;
    u->cts = ctsPin;
    u->rts = rtsPin;
    serialDe(u).setPins(rxPin, txPin, ctsPin, rtsPin);
    return true;
}
extern "C" int8_t uart_get_RxPin(uint8_t uart_num) { return uart_num < 3 ? uarts[uart_num].rx : -1; }
extern "C" int8_t uart_get_TxPin(uint8_t uart_num) { return uart_num < 3 ? uarts[uart_num].tx : -1; }
extern "C" int8_t uart_get_CtsPin(uint8_t uart_num) { return uart_num < 3 ? uarts[uart_num].cts : -1; }
extern "C" int8_t uart_get_RtsPin(uint8_t uart_num) { return uart_num < 3 ? uarts[uart_num].rts : -1; }
extern "C" bool uartSetHwFlowCtrlMode(uart_t *uart, uart_hw_flowcontrol_t mode, uint8_t threshold)
{
    (void)mode;
    (void)threshold;
    return uart != nullptr;
}
extern "C" bool uartSetMode(uart_t *uart, uart_mode_t mode)
{
    (void)mode;
    return uart != nullptr;
}
extern "C" bool uartSetIrdaDirection(uart_t *uart, esp32_uart_irda_direction_t irdaDirection)
{
    (void)irdaDirection;
    return uart != nullptr;
}
extern "C" bool uartSetClockSource(uint8_t uartNum, uart_sclk_t clkSrc)
{
    (void)clkSrc;
    return uartNum < 3;
}
extern "C" bool uartEnableRxInternalPull(uint8_t uartNum, bool enable)
{
    (void)enable;
    return uartNum < 3;
}
extern "C" void uartStartDetectBaudrate(uart_t *uart) { (void)uart; }
/* A taxa da outra ponta: no simulador as duas pontas ja falam a mesma. */
extern "C" unsigned long uartDetectBaudrate(uart_t *uart) { return uart ? uart->baud : 0; }
extern "C" void uart_internal_loopback(uint8_t uartNum, int8_t rxPin)
{
    (void)uartNum;
    (void)rxPin;
}
extern "C" void uart_internal_hw_flow_ctrl_loopback(uint8_t uartNum, int8_t ctsPin)
{
    (void)uartNum;
    (void)ctsPin;
}
extern "C" void uart_send_break(uint8_t uartNum) { (void)uartNum; }
extern "C" int uart_send_msg_with_break(uint8_t uartNum, uint8_t *msg, size_t msgSize)
{
    if (uartNum >= 3) return 0;
    serialDe(&uarts[uartNum]).write(msg, msgSize);
    return (int)msgSize;
}
extern "C" uint16_t uart_get_max_rx_timeout(uint8_t uartNum)
{
    (void)uartNum;
    return 101;
}

/* =====================================================================
 * o SPI por numero de barramento (esp32-hal-spi.h): o SPI e o SPI1 da
 * biblioteca. */
struct spi_struct_t {
    uint8_t num;
    uint32_t div;
    uint8_t modo, ordem;
    int8_t sck, miso, mosi, ss;
    bool ssInvertido;
};
static spi_struct_t spis[4] = {};

static SPIClass &spiDe(spi_t *s) { return s && s->num == 3 ? SPI1 : SPI; }

/* O divisor do relogio do SPI da ESP32 (sobre os 80 MHz do APB). */
extern "C" uint32_t spiClockDivToFrequency(spi_t *spi, uint32_t clockDiv)
{
    (void)spi;
    uint32_t pre = ((clockDiv >> 18) & 0x1FFF) + 1, n = ((clockDiv >> 12) & 0x3F) + 1;
    if (clockDiv & (1u << 31)) return getApbFrequency();
    return getApbFrequency() / (pre * n);
}
extern "C" uint32_t spiFrequencyToClockDiv(spi_t *spi, uint32_t freq)
{
    (void)spi;
    uint32_t apb = getApbFrequency();
    if (freq >= apb) return 1u << 31;
    uint32_t n = 2, pre = (apb / freq + n - 1) / n;
    if (pre > 8192) pre = 8192;
    if (pre < 1) pre = 1;
    return ((pre - 1) << 18) | ((n - 1) << 12) | (((n + 1) / 2 - 1) << 6) | (n - 1);
}

extern "C" spi_t *spiStartBus(uint8_t spi_num, uint32_t clockDiv, uint8_t dataMode, uint8_t bitOrder)
{
    if (spi_num >= 4) return nullptr;
    spi_t *s = &spis[spi_num];
    s->num = spi_num;
    s->div = clockDiv;
    s->modo = dataMode;
    s->ordem = bitOrder;
    s->sck = s->miso = s->mosi = s->ss = -1;
    return s;
}
extern "C" void spiStopBus(spi_t *spi)
{
    if (spi) spiDe(spi).end();
}
static void comecarSpi(spi_t *s)
{
    spiDe(s).begin(s->sck, s->miso, s->mosi, s->ss);
}
extern "C" bool spiAttachSCK(spi_t *spi, int8_t sck)
{
    if (!spi) return false;
    spi->sck = sck;
    comecarSpi(spi);
    return true;
}
extern "C" bool spiAttachMISO(spi_t *spi, int8_t miso)
{
    if (!spi) return false;
    spi->miso = miso;
    comecarSpi(spi);
    return true;
}
extern "C" bool spiAttachMOSI(spi_t *spi, int8_t mosi)
{
    if (!spi) return false;
    spi->mosi = mosi;
    comecarSpi(spi);
    return true;
}
extern "C" bool spiDetachSCK(spi_t *spi) { return spi && ((spi->sck = -1), true); }
extern "C" bool spiDetachMISO(spi_t *spi) { return spi && ((spi->miso = -1), true); }
extern "C" bool spiDetachMOSI(spi_t *spi) { return spi && ((spi->mosi = -1), true); }
extern "C" bool spiAttachSS(spi_t *spi, uint8_t ss_num, int8_t ss)
{
    (void)ss_num;
    if (!spi) return false;
    spi->ss = ss;
    if (ss >= 0) {
        pinMode(ss, OUTPUT);
        digitalWrite(ss, spi->ssInvertido ? LOW : HIGH);
    }
    return true;
}
extern "C" bool spiDetachSS(spi_t *spi) { return spi && ((spi->ss = -1), true); }
extern "C" void spiEnableSSPins(spi_t *spi, uint8_t ss_mask) { (void)spi, (void)ss_mask; }
extern "C" void spiDisableSSPins(spi_t *spi, uint8_t ss_mask) { (void)spi, (void)ss_mask; }
extern "C" void spiSSEnable(spi_t *spi) { (void)spi; }
extern "C" void spiSSDisable(spi_t *spi) { (void)spi; }
extern "C" void spiSSSet(spi_t *spi)
{
    if (spi && spi->ss >= 0) digitalWrite(spi->ss, spi->ssInvertido ? HIGH : LOW);
}
extern "C" void spiSSClear(spi_t *spi)
{
    if (spi && spi->ss >= 0) digitalWrite(spi->ss, spi->ssInvertido ? LOW : HIGH);
}
extern "C" void spiWaitReady(spi_t *spi) { (void)spi; }
extern "C" void spiSSInvert(spi_t *spi, bool invert)
{
    if (spi) spi->ssInvertido = invert;
}
extern "C" uint32_t spiGetClockDiv(spi_t *spi) { return spi ? spi->div : 0; }
extern "C" uint8_t spiGetDataMode(spi_t *spi) { return spi ? spi->modo : 0; }
extern "C" uint8_t spiGetBitOrder(spi_t *spi) { return spi ? spi->ordem : 0; }
extern "C" void spiSetClockDiv(spi_t *spi, uint32_t clockDiv)
{
    if (!spi) return;
    spi->div = clockDiv;
    spiDe(spi).setFrequency(spiClockDivToFrequency(spi, clockDiv));
}
extern "C" void spiSetDataMode(spi_t *spi, uint8_t dataMode)
{
    if (!spi) return;
    spi->modo = dataMode;
    spiDe(spi).setDataMode(dataMode);
}
extern "C" void spiSetBitOrder(spi_t *spi, uint8_t bitOrder)
{
    if (!spi) return;
    spi->ordem = bitOrder;
    spiDe(spi).setBitOrder(bitOrder);
}
extern "C" void spiWrite(spi_t *spi, const uint32_t *data, uint8_t len)
{
    for (uint8_t i = 0; i < len; i++) spiDe(spi).write32(data[i]);
}
extern "C" void spiWriteByte(spi_t *spi, uint8_t data) { spiDe(spi).write(data); }
extern "C" void spiWriteWord(spi_t *spi, uint16_t data) { spiDe(spi).write16(data); }
extern "C" void spiWriteLong(spi_t *spi, uint32_t data) { spiDe(spi).write32(data); }
extern "C" void spiTransfer(spi_t *spi, uint32_t *out, uint8_t len)
{
    for (uint8_t i = 0; i < len; i++) out[i] = spiDe(spi).transfer32(out[i]);
}
extern "C" uint8_t spiTransferByte(spi_t *spi, uint8_t data) { return spiDe(spi).transfer(data); }
extern "C" uint16_t spiTransferWord(spi_t *spi, uint16_t data) { return spiDe(spi).transfer16(data); }
extern "C" uint32_t spiTransferLong(spi_t *spi, uint32_t data) { return spiDe(spi).transfer32(data); }
extern "C" void spiTransferBytes(spi_t *spi, const uint8_t *data, uint8_t *out, uint32_t size)
{
    spiDe(spi).transferBytes(data, out, size);
}
extern "C" void spiTransferBits(spi_t *spi, uint32_t data, uint32_t *out, uint8_t bits)
{
    uint32_t r = bits <= 8 ? spiDe(spi).transfer((uint8_t)data) : bits <= 16 ? spiDe(spi).transfer16((uint16_t)data) : spiDe(spi).transfer32(data);
    if (out) *out = r;
}
extern "C" void spiTransaction(spi_t *spi, uint32_t clockDiv, uint8_t dataMode, uint8_t bitOrder)
{
    if (!spi) return;
    spi->div = clockDiv;
    spi->modo = dataMode;
    spi->ordem = bitOrder;
    spiDe(spi).beginTransaction(SPISettings(spiClockDivToFrequency(spi, clockDiv), bitOrder, dataMode));
}
extern "C" void spiSimpleTransaction(spi_t *spi) { (void)spi; }
extern "C" void spiEndTransaction(spi_t *spi) { spiDe(spi).endTransaction(); }
extern "C" void spiWriteNL(spi_t *spi, const void *data_in, uint32_t len) { spiDe(spi).writeBytes((const uint8_t *)data_in, len); }
extern "C" void spiWriteByteNL(spi_t *spi, uint8_t data) { spiDe(spi).write(data); }
extern "C" void spiWriteShortNL(spi_t *spi, uint16_t data) { spiDe(spi).write16(data); }
extern "C" void spiWriteLongNL(spi_t *spi, uint32_t data) { spiDe(spi).write32(data); }
extern "C" void spiWritePixelsNL(spi_t *spi, const void *data_in, uint32_t len) { spiDe(spi).writeBytes((const uint8_t *)data_in, len); }
extern "C" uint8_t spiTransferByteNL(spi_t *spi, uint8_t data) { return spiDe(spi).transfer(data); }
extern "C" uint16_t spiTransferShortNL(spi_t *spi, uint16_t data) { return spiDe(spi).transfer16(data); }
extern "C" uint32_t spiTransferLongNL(spi_t *spi, uint32_t data) { return spiDe(spi).transfer32(data); }
extern "C" void spiTransferBytesNL(spi_t *spi, const void *data_in, uint8_t *data_out, uint32_t len)
{
    spiDe(spi).transferBytes((const uint8_t *)data_in, data_out, len);
}
extern "C" void spiTransferBitsNL(spi_t *spi, uint32_t data_in, uint32_t *data_out, uint8_t bits)
{
    spiTransferBits(spi, data_in, data_out, bits);
}

/* =====================================================================
 * o I2C por numero de barramento (esp32-hal-i2c.h e -i2c-slave.h): o Wire
 * e o Wire1 da biblioteca. */
static TwoWire &wireDe(uint8_t num) { return num == 1 ? Wire1 : Wire; }
static bool i2cIniciado[2];
static uint32_t i2cRelogio[2] = {100000, 100000};

extern "C" esp_err_t i2cInit(uint8_t i2c_num, int8_t sda, int8_t scl, uint32_t clk_speed)
{
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    if (!wireDe(i2c_num).begin(sda, scl, clk_speed)) return ESP_FAIL;
    i2cIniciado[i2c_num] = true;
    i2cRelogio[i2c_num] = clk_speed ? clk_speed : 100000;
    return ESP_OK;
}
extern "C" esp_err_t i2cDeinit(uint8_t i2c_num)
{
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    wireDe(i2c_num).end();
    i2cIniciado[i2c_num] = false;
    return ESP_OK;
}
extern "C" esp_err_t i2cSetClock(uint8_t i2c_num, uint32_t frequency)
{
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    wireDe(i2c_num).setClock(frequency);
    i2cRelogio[i2c_num] = frequency;
    return ESP_OK;
}
extern "C" esp_err_t i2cGetClock(uint8_t i2c_num, uint32_t *frequency)
{
    if (i2c_num > 1 || !frequency) return ESP_ERR_INVALID_ARG;
    *frequency = i2cRelogio[i2c_num];
    return ESP_OK;
}
extern "C" bool i2cIsInit(uint8_t i2c_num) { return i2c_num < 2 && i2cIniciado[i2c_num]; }
extern "C" void *i2cBusHandle(uint8_t i2c_num) { return i2c_num < 2 ? (void *)&wireDe(i2c_num) : nullptr; }

static esp_err_t erroDoWire(uint8_t r)
{
    switch (r) {
    case 0: return ESP_OK;
    case 5: return ESP_ERR_TIMEOUT;
    default: return ESP_FAIL;
    }
}
extern "C" esp_err_t i2cWrite(uint8_t i2c_num, uint16_t address, const uint8_t *buff, size_t size, uint32_t timeOutMillis)
{
    (void)timeOutMillis;
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    TwoWire &w = wireDe(i2c_num);
    w.beginTransmission((uint8_t)address);
    if (size) w.write(buff, size);
    return erroDoWire(w.endTransmission(true));
}
extern "C" esp_err_t i2cRead(uint8_t i2c_num, uint16_t address, uint8_t *buff, size_t size, uint32_t timeOutMillis, size_t *readCount)
{
    (void)timeOutMillis;
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    TwoWire &w = wireDe(i2c_num);
    size_t n = w.requestFrom((uint8_t)address, size, true);
    size_t k = 0;
    while (k < n && w.available()) buff[k++] = (uint8_t)w.read();
    if (readCount) *readCount = k;
    return k == size ? ESP_OK : ESP_FAIL;
}
extern "C" esp_err_t i2cWriteReadNonStop(uint8_t i2c_num, uint16_t address, const uint8_t *wbuff, size_t wsize, uint8_t *rbuff, size_t rsize,
                                         uint32_t timeOutMillis, size_t *readCount)
{
    (void)timeOutMillis;
    if (i2c_num > 1) return ESP_ERR_INVALID_ARG;
    TwoWire &w = wireDe(i2c_num);
    w.beginTransmission((uint8_t)address);
    if (wsize) w.write(wbuff, wsize);
    esp_err_t r = erroDoWire(w.endTransmission(false));
    if (r != ESP_OK) return r;
    size_t n = w.requestFrom((uint8_t)address, rsize, true);
    size_t k = 0;
    while (k < n && w.available()) rbuff[k++] = (uint8_t)w.read();
    if (readCount) *readCount = k;
    return k == rsize ? ESP_OK : ESP_FAIL;
}

#if SOC_I2C_SUPPORT_SLAVE
#include "esp32-hal-i2c-slave.h"
/* A placa como escrava de I2C pede outra placa como mestre no mesmo
 * barramento, rodando o proprio sketch -- o simulador ainda nao liga duas
 * placas assim. Como o Wire.onReceive do interpretador, a recusa e dita. */
static esp_err_t semEscravo()
{
    static bool avisou = false;
    if (!avisou) {
        avisou = true;
        log_printf("[I2C] a placa como ESCRAVA de I2C ainda nao e simulada: o simulador roda a placa como mestre.\n");
    }
    return ESP_ERR_NOT_SUPPORTED;
}
extern "C" esp_err_t i2cSlaveAttachCallbacks(uint8_t, i2c_slave_request_cb_t, i2c_slave_receive_cb_t, void *) { return semEscravo(); }
extern "C" esp_err_t i2cSlaveInit(uint8_t, int, int, uint16_t, uint32_t, size_t, size_t) { return semEscravo(); }
extern "C" esp_err_t i2cSlaveDeinit(uint8_t) { return ESP_OK; }
extern "C" size_t i2cSlaveWrite(uint8_t, const uint8_t *, uint32_t, uint32_t) { return 0; }
#endif

/* =====================================================================
 * os relatorios do core (freertos_stats.h, chip-debug-report.h) */
#include "freertos_stats.h"
#include "chip-debug-report.h"

void printRunningTasks(Print &printer)
{
    printer.printf("Tasks: %u\n", (unsigned)uxTaskGetNumberOfTasks());
    printer.printf("Free heap: %u bytes\n", (unsigned)ESP.getFreeHeap());
}

static void linhaDoRelatorio(const char *nome, const char *valor) { log_printf("  %-22s: %s\n", nome, valor); }

void printBeforeSetupInfo(void)
{
    char t[64];
    log_printf("=========== Before Setup Start ===========\n");
    log_printf("Chip Info:\n------------------------------------------\n");
    linhaDoRelatorio("Model", ESP.getChipModel());
    snprintf(t, sizeof(t), "%u", (unsigned)ESP.getChipRevision());
    linhaDoRelatorio("Revision", t);
    snprintf(t, sizeof(t), "%u MHz", (unsigned)getCpuFrequencyMhz());
    linhaDoRelatorio("Cpu Frequency", t);
    snprintf(t, sizeof(t), "%u MHz", (unsigned)getXtalFrequencyMhz());
    linhaDoRelatorio("XTAL Frequency", t);
    snprintf(t, sizeof(t), "%u", (unsigned)ESP.getChipCores());
    linhaDoRelatorio("Cores", t);
    log_printf("------------------------------------------\nINTERNAL Memory Info:\n------------------------------------------\n");
    snprintf(t, sizeof(t), "%u B", (unsigned)ESP.getHeapSize());
    linhaDoRelatorio("Total Size", t);
    snprintf(t, sizeof(t), "%u B", (unsigned)ESP.getFreeHeap());
    linhaDoRelatorio("Free Bytes", t);
    log_printf("------------------------------------------\nFlash Info:\n------------------------------------------\n");
    snprintf(t, sizeof(t), "%u B", (unsigned)ESP.getFlashChipSize());
    linhaDoRelatorio("Chip Size", t);
    log_printf("============ Before Setup End ============\n");
}

void printAfterSetupInfo(void)
{
    char t[64];
    log_printf("=========== After Setup Start ============\n");
    snprintf(t, sizeof(t), "%u B", (unsigned)ESP.getFreeHeap());
    linhaDoRelatorio("Free Bytes", t);
    snprintf(t, sizeof(t), "%u B", (unsigned)ESP.getMinFreeHeap());
    linhaDoRelatorio("Minimum Free Bytes", t);
    log_printf("============ After Setup End =============\n");
}

#endif /* ESP32 */
