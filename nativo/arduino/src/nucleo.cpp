/* nucleo.cpp -- as funcoes do core do Arduino.
 *
 * As que mexem no hardware vao ao simulador pelo nome, e la caem na mesma
 * implementacao do interpretador: o delay() que atende a interrupcao na
 * borda certa, o analogRead() que custa o tempo do conversor, o pulseIn()
 * que mede o sinal do receptor. As contas ficam aqui. */
#include "cs_interno.h"

extern "C" __declspec(dllimport) int __stdcall SwitchToThread(void);

extern "C" {

void pinMode(uint8_t pin, uint8_t mode) { ch(nullptr, "pinMode", cs_i(pin), cs_i(mode)); }
void digitalWrite(uint8_t pin, uint8_t val) { ch(nullptr, "digitalWrite", cs_i(pin), cs_i(val)); }
int digitalRead(uint8_t pin) { return (int)ch_i(nullptr, "digitalRead", cs_i(pin)); }
#ifndef ESP32 /* na ESP32, o do cs_hal.cpp (com a atenuacao) */
int analogRead(uint8_t pin) { return (int)ch_i(nullptr, "analogRead", cs_i(pin)); }
#endif
void analogReference(uint8_t mode) { ch(nullptr, "analogReference", cs_i(mode)); }
void analogWrite(uint8_t pin, int val) { ch(nullptr, "analogWrite", cs_i(pin), cs_i(val)); }
#ifdef ESP32
void analogReadResolution(uint8_t bits) { ch(nullptr, "analogReadResolution", cs_i(bits)); }
#else
void analogReadResolution(int bits) { ch(nullptr, "analogReadResolution", cs_i(bits)); }
#endif
void analogWriteResolution(int bits) { ch(nullptr, "analogWriteResolution", cs_i(bits)); }
void analogWriteFrequency(uint32_t freq) { ch(nullptr, "analogWriteFrequency", cs_i(freq)); }
void analogWriteRange(uint32_t range) { ch(nullptr, "analogWriteRange", cs_i(range)); }

/* O relogio direto, sem o interpretador: micros() num laco apertado e comum. */
unsigned long micros(void)
{
    if (!cs_p) return 0;
    int64_t us = cs_relogio();
    if (us < 0) cs_sair_com(CS_SAIR);
    return (unsigned long)(uint64_t)us;
}

unsigned long millis(void)
{
    if (!cs_p) return 0;
    int64_t us = cs_relogio();
    if (us < 0) cs_sair_com(CS_SAIR);
    return (unsigned long)((uint64_t)us / 1000);
}

extern volatile int cs_rtos_ativo;
void vTaskDelay(const uint32_t ticks);
void cs_rtos_ceder(void);

/* Com o FreeRTOS no jogo, o delay e o vTaskDelay (a vez passa para as outras
 * tarefas, como na ESP32); sem ele, o delay do simulador. */
#ifdef ESP32
void delay(uint32_t ms)
#else
void delay(unsigned long ms)
#endif
{
    if (cs_rtos_ativo) vTaskDelay((uint32_t)ms);
    else ch(nullptr, "delay", cs_i((int64_t)ms));
}
void delayMicroseconds(unsigned int us) { ch(nullptr, "delayMicroseconds", cs_i(us)); }

/* O yield() so cede: a placa parada sai por aqui tambem. */
void yield(void)
{
    if (!cs_p) return;
    if (cs_relogio() < 0) cs_sair_com(CS_SAIR);
    if (cs_rtos_ativo) cs_rtos_ceder();
    else SwitchToThread();
}

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout)
{
    return (unsigned long)ch_i(nullptr, "pulseIn", cs_i(pin), cs_i(state), cs_i((int64_t)timeout));
}

unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout)
{
    return (unsigned long)ch_i(nullptr, "pulseInLong", cs_i(pin), cs_i(state), cs_i((int64_t)timeout));
}

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val)
{
    ch(nullptr, "shiftOut", cs_i(dataPin), cs_i(clockPin), cs_i(bitOrder), cs_i(val));
}

uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder)
{
    return (uint8_t)ch_i(nullptr, "shiftIn", cs_i(dataPin), cs_i(clockPin), cs_i(bitOrder));
}

/* A rotina vira um nome que o simulador chama de volta ("__nativa_3"). */
/* A interrupcao de cada pino (o enableInterrupt/disableInterrupt da ESP32
 * religa a mesma). */
cs_interrupcao_t cs_interrupcoes[CS_PINOS_COM_INTERRUPCAO];

static void ligarInterrupcao(uint8_t pino, int rotina, int modo)
{
    char nome[24];
    snprintf(nome, sizeof(nome), "__nativa_%d", rotina);
    ch(nullptr, "attachInterrupt", cs_i(pino), cs_nome(nome), cs_i(modo));
}

void cs_religar_interrupcao(uint8_t pino)
{
    if (pino < CS_PINOS_COM_INTERRUPCAO && cs_interrupcoes[pino].rotina >= 0 && cs_interrupcoes[pino].ligada)
        ligarInterrupcao(pino, cs_interrupcoes[pino].rotina, cs_interrupcoes[pino].modo);
}

void attachInterrupt(uint8_t interruptNum, void (*userFunc)(void), int mode)
{
    int i = cs_registrar_rotina(userFunc, nullptr, nullptr);
    if (i < 0) return;
    if (interruptNum < CS_PINOS_COM_INTERRUPCAO) cs_interrupcoes[interruptNum] = {i, mode, true};
    ligarInterrupcao(interruptNum, i, mode);
}

void detachInterrupt(uint8_t interruptNum)
{
    if (interruptNum < CS_PINOS_COM_INTERRUPCAO) cs_interrupcoes[interruptNum] = {-1, 0, false};
    ch(nullptr, "detachInterrupt", cs_i(interruptNum));
}
int digitalPinToInterrupt(int pin) { return (int)ch_i(nullptr, "digitalPinToInterrupt", cs_i(pin)); }
void interrupts(void) { ch(nullptr, "interrupts"); }
void noInterrupts(void) { ch(nullptr, "noInterrupts"); }

void cs_tone(uint8_t pin, unsigned int frequency, unsigned long duration)
{
    if (duration) ch(nullptr, "tone", cs_i(pin), cs_i(frequency), cs_i((int64_t)duration));
    else ch(nullptr, "tone", cs_i(pin), cs_i(frequency));
}

void noTone(uint8_t pin) { ch(nullptr, "noTone", cs_i(pin)); }

long cs_random(long howsmall, long howbig)
{
    if (howsmall >= howbig) return howsmall;
    return (long)ch_i(nullptr, "random", cs_i(howsmall), cs_i(howbig));
}

void randomSeed(unsigned long seed)
{
    if (seed != 0) ch(nullptr, "randomSeed", cs_i((int64_t)seed));
}

long map(long x, long in_min, long in_max, long out_min, long out_max)
{
    if (in_max == in_min) return out_min;
    return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
}

unsigned int makeWord(unsigned char h, unsigned char l) { return ((unsigned int)h << 8) | l; }

/* ---- o stdlib do AVR ---- */
#ifdef ESP32
char *dtostrf(double val, signed int width, unsigned int prec, char *sout)
#else
char *dtostrf(double val, signed char width, unsigned char prec, char *sout)
#endif
{
    char fmt[20];
    snprintf(fmt, sizeof(fmt), "%%%d.%df", width, prec);
    sprintf(sout, fmt, val);
    return sout;
}

static char *paraTexto(unsigned long long valor, bool negativo, char *str, int radix)
{
    if (radix < 2 || radix > 36) {
        *str = 0;
        return str;
    }
    char tmp[72];
    int i = 0;
    do {
        int d = (int)(valor % (unsigned)radix);
        tmp[i++] = (char)(d < 10 ? '0' + d : 'a' + d - 10);
        valor /= (unsigned)radix;
    } while (valor);
    char *p = str;
    if (negativo) *p++ = '-';
    while (i) *p++ = tmp[--i];
    *p = 0;
    return str;
}

char *ltoa(long value, char *str, int radix)
{
    if (radix == 10 && value < 0) return paraTexto((unsigned long long)(-(long long)value), true, str, radix);
    return paraTexto((unsigned long)value, false, str, radix);
}

char *ultoa(unsigned long value, char *str, int radix) { return paraTexto(value, false, str, radix); }
char *utoa(unsigned int value, char *str, int radix) { return paraTexto(value, false, str, radix); }

char *itoa(int value, char *str, int radix)
{
    if (radix == 10 && value < 0) return paraTexto((unsigned long long)(-(long long)value), true, str, radix);
    return paraTexto((unsigned int)value, false, str, radix);
}

#ifdef ESP32
double ledcSetup(uint8_t channel, double freq, uint8_t bits)
{
    return ch_r(nullptr, "ledcSetup", cs_i(channel), cs_r(freq), cs_i(bits));
}
void ledcAttachPin(uint8_t pin, uint8_t channel) { ch(nullptr, "ledcAttachPin", cs_i(pin), cs_i(channel)); }
void ledcDetachPin(uint8_t pin) { ch(nullptr, "ledcDetachPin", cs_i(pin)); }
cs_ledc_t cs_ledc[CS_PINOS_COM_INTERRUPCAO];

static void guardarLedc(uint8_t pino, uint32_t freq, uint8_t bits)
{
    if (pino >= CS_PINOS_COM_INTERRUPCAO) return;
    cs_ledc[pino].freq = freq;
    if (bits) cs_ledc[pino].bits = bits;
}

bool ledcAttach(uint8_t pin, uint32_t freq, uint8_t res)
{
    guardarLedc(pin, freq, res);
    return ch_i(nullptr, "ledcAttach", cs_i(pin), cs_i(freq), cs_i(res)) != 0;
}
bool ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t res, uint8_t channel)
{
    guardarLedc(pin, freq, res);
    return ch_i(nullptr, "ledcAttachChannel", cs_i(pin), cs_i(freq), cs_i(res), cs_i(channel)) != 0;
}
bool ledcDetach(uint8_t pin) { return ch_i(nullptr, "ledcDetach", cs_i(pin)) != 0; }
bool ledcWrite(uint8_t chan_or_pin, uint32_t duty)
{
    /* A saida invertida (ledcOutputInvert): o ciclo de cabeca para baixo. */
    if (chan_or_pin < CS_PINOS_COM_INTERRUPCAO && cs_ledc[chan_or_pin].invertido) {
        uint32_t maximo = (1u << (cs_ledc[chan_or_pin].bits ? cs_ledc[chan_or_pin].bits : 8)) - 1;
        duty = duty > maximo ? 0 : maximo - duty;
    }
    return ch_i(nullptr, "ledcWrite", cs_i(chan_or_pin), cs_i(duty)) != 0;
}
uint32_t ledcRead(uint8_t chan_or_pin) { return (uint32_t)ch_i(nullptr, "ledcRead", cs_i(chan_or_pin)); }
uint32_t ledcWriteTone(uint8_t chan_or_pin, uint32_t freq)
{
    guardarLedc(chan_or_pin, freq, 0);
    return (uint32_t)ch_r(nullptr, "ledcWriteTone", cs_i(chan_or_pin), cs_r(freq));
}
uint32_t ledcChangeFrequency(uint8_t chan_or_pin, uint32_t freq, uint8_t res)
{
    guardarLedc(chan_or_pin, freq, res);
    return (uint32_t)ch_r(nullptr, "ledcChangeFrequency", cs_i(chan_or_pin), cs_i(freq), cs_i(res));
}
uint32_t analogReadMilliVolts(uint8_t pin) { return (uint32_t)ch_i(nullptr, "analogReadMilliVolts", cs_i(pin)); }
void neopixelWrite(uint8_t pin, uint8_t r, uint8_t g, uint8_t b) { ch(nullptr, "neopixelWrite", cs_i(pin), cs_i(r), cs_i(g), cs_i(b)); }
void rgbLedWrite(uint8_t pin, uint8_t r, uint8_t g, uint8_t b) { ch(nullptr, "rgbLedWrite", cs_i(pin), cs_i(r), cs_i(g), cs_i(b)); }
float temperatureRead(void) { return (float)ch_r(nullptr, "temperatureRead"); }

/* ---- o sono e o reinicio (esp_sleep.h, esp_system.h) ---- */
/* O EXT0 e o EXT1 so existem nos chips que tem (o esp_sleep.h de verdade
 * nem os declara na C3, por exemplo). */
#if SOC_PM_SUPPORT_EXT0_WAKEUP
esp_err_t esp_sleep_enable_ext0_wakeup(gpio_num_t gpio, int level)
{
    return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_ext0_wakeup", cs_i(gpio), cs_i(level));
}
#endif
#if SOC_PM_SUPPORT_EXT1_WAKEUP
esp_err_t esp_sleep_enable_ext1_wakeup(uint64_t mask, esp_sleep_ext1_wakeup_mode_t mode)
{
    return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_ext1_wakeup", cs_i((int64_t)mask), cs_i(mode));
}
esp_err_t esp_sleep_enable_ext1_wakeup_io(uint64_t mask, esp_sleep_ext1_wakeup_mode_t mode)
{
    return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_ext1_wakeup_io", cs_i((int64_t)mask), cs_i(mode));
}
#endif
esp_err_t esp_sleep_enable_timer_wakeup(uint64_t us)
{
    return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_timer_wakeup", cs_i((int64_t)us));
}
esp_err_t esp_sleep_enable_touchpad_wakeup(void) { return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_touchpad_wakeup"); }
esp_err_t esp_sleep_enable_gpio_wakeup(void) { return (esp_err_t)ch_i(nullptr, "esp_sleep_enable_gpio_wakeup"); }
esp_err_t esp_sleep_disable_wakeup_source(esp_sleep_source_t source)
{
    return (esp_err_t)ch_i(nullptr, "esp_sleep_disable_wakeup_source", cs_i(source));
}
esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void) { return (esp_sleep_wakeup_cause_t)ch_i(nullptr, "esp_sleep_get_wakeup_cause"); }
uint64_t esp_sleep_get_ext1_wakeup_status(void) { return (uint64_t)ch_i(nullptr, "esp_sleep_get_ext1_wakeup_status"); }
void esp_deep_sleep_start(void) { ch(nullptr, "esp_deep_sleep_start"); }
esp_err_t esp_light_sleep_start(void) { return (esp_err_t)ch_i(nullptr, "esp_light_sleep_start"); }
void esp_deep_sleep(uint64_t us) { ch(nullptr, "esp_deep_sleep", cs_i((int64_t)us)); }
void esp_restart(void) { ch(nullptr, "esp_restart"); }
esp_reset_reason_t esp_reset_reason(void) { return (esp_reset_reason_t)ch_i(nullptr, "esp_reset_reason"); }
#endif

} /* extern "C" */

unsigned int makeWord(unsigned int w) { return w; }

long random(long howbig) { return cs_random(0, howbig); }
long random(long howsmall, long howbig) { return cs_random(howsmall, howbig); }

void attachInterruptArg(uint8_t pin, void (*userFunc)(void *), void *arg, int mode)
{
    int i = cs_registrar_rotina(nullptr, userFunc, arg);
    if (i < 0) return;
    if (pin < CS_PINOS_COM_INTERRUPCAO) cs_interrupcoes[pin] = {i, mode, true};
    ligarInterrupcao(pin, i, mode);
}

/* ---- o objeto ESP ---- */
#if defined(ESP32) || defined(ESP8266)
EspClass ESP;

static const char *guardarTexto(const String &s)
{
    static thread_local char buf[64];
    snprintf(buf, sizeof(buf), "%s", s.c_str());
    return buf;
}

void EspClass::restart() { ch("ESP", "restart"); }
uint32_t EspClass::getFreeHeap() { return (uint32_t)ch_i("ESP", "getFreeHeap"); }
uint32_t EspClass::getHeapSize() { return (uint32_t)ch_i("ESP", "getHeapSize"); }
uint32_t EspClass::getMinFreeHeap() { return (uint32_t)ch_i("ESP", "getMinFreeHeap"); }
uint32_t EspClass::getMaxAllocHeap() { return (uint32_t)ch_i("ESP", "getMaxAllocHeap"); }
uint32_t EspClass::getPsramSize() { return (uint32_t)ch_i("ESP", "getPsramSize"); }
uint32_t EspClass::getFreePsram() { return (uint32_t)ch_i("ESP", "getFreePsram"); }
uint32_t EspClass::getCpuFreqMHz() { return (uint32_t)ch_i("ESP", "getCpuFreqMHz"); }
uint32_t EspClass::getFlashChipSize() { return (uint32_t)ch_i("ESP", "getFlashChipSize"); }
uint32_t EspClass::getSketchSize() { return (uint32_t)ch_i("ESP", "getSketchSize"); }
uint32_t EspClass::getFreeSketchSpace() { return (uint32_t)ch_i("ESP", "getFreeSketchSpace"); }
uint64_t EspClass::getEfuseMac() { return (uint64_t)ch_i("ESP", "getEfuseMac"); }
uint32_t EspClass::getChipId() { return (uint32_t)ch_i("ESP", "getChipId"); }
const char *EspClass::getChipModel() { return guardarTexto(ch_t("ESP", "getChipModel")); }
uint8_t EspClass::getChipRevision() { return (uint8_t)ch_i("ESP", "getChipRevision"); }
uint8_t EspClass::getChipCores() { return (uint8_t)ch_i("ESP", "getChipCores"); }
const char *EspClass::getSdkVersion() { return guardarTexto(ch_t("ESP", "getSdkVersion")); }
uint32_t EspClass::getCycleCount() { return (uint32_t)ch_i("ESP", "getCycleCount"); }
void EspClass::deepSleep(uint64_t time_us) { ch("ESP", "deepSleep", cs_i((int64_t)time_us)); }

void configTime(long gmtOffset_sec, int daylightOffset_sec, const char *server1, const char *server2, const char *server3)
{
    ch(nullptr, "configTime", cs_i(gmtOffset_sec), cs_i(daylightOffset_sec), cs_t(server1 ? server1 : "pool.ntp.org"),
       cs_t(server2 ? server2 : ""), cs_t(server3 ? server3 : ""));
}

void configTzTime(const char *tz, const char *server1, const char *server2, const char *server3)
{
    ch(nullptr, "configTzTime", cs_t(tz), cs_t(server1 ? server1 : "pool.ntp.org"), cs_t(server2 ? server2 : ""),
       cs_t(server3 ? server3 : ""));
}

/* O horario num struct tm do simulador, copiado campo a campo para o do sketch. */
bool getLocalTime(struct tm *info, uint32_t ms)
{
    (void)ms;
    cs_val criar[] = {cs_t("tm")};
    String nome = cs_texto(nullptr, "__criar", 1, criar);
    bool ok = ch_i(nullptr, "getLocalTime", cs_nome(nome.c_str())) != 0;
    if (ok && info) {
        auto campo = [&](const char *c) {
            cs_val a[] = {cs_t(nome.c_str()), cs_t(c)};
            return (int)cs_int(nullptr, "__ler", 2, a);
        };
        memset(info, 0, sizeof(*info));
        info->tm_sec = campo("tm_sec");
        info->tm_min = campo("tm_min");
        info->tm_hour = campo("tm_hour");
        info->tm_mday = campo("tm_mday");
        info->tm_mon = campo("tm_mon");
        info->tm_year = campo("tm_year");
        info->tm_wday = campo("tm_wday");
        info->tm_yday = campo("tm_yday");
        info->tm_isdst = campo("tm_isdst");
    }
    ch(nullptr, "__destruir", cs_t(nome.c_str()));
    return ok;
}
#endif
