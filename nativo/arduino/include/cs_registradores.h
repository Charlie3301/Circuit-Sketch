/* cs_registradores.h -- os registradores de GPIO da ESP32 (soc/gpio_reg.h,
 * soc/gpio_struct.h) e o timer de hardware (esp32-hal-timer.h, a API do
 * core 3.x e a do 2.x).
 *
 * O REG_WRITE(GPIO_OUT_W1TS_REG, mascara) mexe nos pinos de verdade do
 * simulador. Pino chaveado depressa (o som feito na mao, numa interrupcao
 * de timer a 40 kHz) vira o tom que ele forma -- a frequencia medida pelas
 * bordas, no tempo de cada interrupcao --, e o buzzer toca esse tom. */
#ifndef CS_REGISTRADORES_H
#define CS_REGISTRADORES_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ---- os enderecos (ESP32) ---- */
#define DR_REG_GPIO_BASE 0x3ff44000
#define GPIO_OUT_REG (DR_REG_GPIO_BASE + 0x0004)
#define GPIO_OUT_W1TS_REG (DR_REG_GPIO_BASE + 0x0008)
#define GPIO_OUT_W1TC_REG (DR_REG_GPIO_BASE + 0x000c)
#define GPIO_OUT1_REG (DR_REG_GPIO_BASE + 0x0010)
#define GPIO_OUT1_W1TS_REG (DR_REG_GPIO_BASE + 0x0014)
#define GPIO_OUT1_W1TC_REG (DR_REG_GPIO_BASE + 0x0018)
#define GPIO_ENABLE_REG (DR_REG_GPIO_BASE + 0x0020)
#define GPIO_ENABLE_W1TS_REG (DR_REG_GPIO_BASE + 0x0024)
#define GPIO_ENABLE_W1TC_REG (DR_REG_GPIO_BASE + 0x0028)
#define GPIO_ENABLE1_REG (DR_REG_GPIO_BASE + 0x002c)
#define GPIO_ENABLE1_W1TS_REG (DR_REG_GPIO_BASE + 0x0030)
#define GPIO_ENABLE1_W1TC_REG (DR_REG_GPIO_BASE + 0x0034)
#define GPIO_IN_REG (DR_REG_GPIO_BASE + 0x003c)
#define GPIO_IN1_REG (DR_REG_GPIO_BASE + 0x0040)

void cs_reg_escrever(uint32_t endereco, uint32_t valor);
uint32_t cs_reg_ler(uint32_t endereco);

#define REG_WRITE(r, v) cs_reg_escrever((uint32_t)(r), (uint32_t)(v))
#define REG_READ(r) cs_reg_ler((uint32_t)(r))
#define REG_SET_BIT(r, b) cs_reg_escrever((uint32_t)(r), cs_reg_ler((uint32_t)(r)) | (uint32_t)(b))
#define REG_CLR_BIT(r, b) cs_reg_escrever((uint32_t)(r), cs_reg_ler((uint32_t)(r)) & ~(uint32_t)(b))
#define REG_GET_BIT(r, b) (cs_reg_ler((uint32_t)(r)) & (uint32_t)(b))
#define WRITE_PERI_REG(r, v) REG_WRITE(r, v)
#define READ_PERI_REG(r) REG_READ(r)
#define SET_PERI_REG_MASK(r, m) REG_SET_BIT(r, m)
#define CLEAR_PERI_REG_MASK(r, m) REG_CLR_BIT(r, m)
#define GPIO_REG_WRITE(r, v) REG_WRITE(r, v)
#define GPIO_REG_READ(r) REG_READ(r)

/* ---- o timer de hardware ---- */
typedef struct hw_timer_s hw_timer_t;

/* core 3.x */
hw_timer_t *cs_timer_comecar(uint32_t frequencia);
void timerEnd(hw_timer_t *timer);
void timerStart(hw_timer_t *timer);
void timerStop(hw_timer_t *timer);
void timerRestart(hw_timer_t *timer);
void timerWrite(hw_timer_t *timer, uint64_t val);
uint64_t timerRead(hw_timer_t *timer);
uint64_t timerReadMicros(hw_timer_t *timer);
uint64_t timerReadMilis(hw_timer_t *timer);
double timerReadSeconds(hw_timer_t *timer);
uint32_t timerGetFrequency(hw_timer_t *timer);
void cs_timer_rotina(hw_timer_t *timer, void (*fn)(void));
void timerAttachInterruptArg(hw_timer_t *timer, void (*fn)(void *), void *arg);
void timerDetachInterrupt(hw_timer_t *timer);
void timerAlarm(hw_timer_t *timer, uint64_t alarm_value, bool autoreload, uint64_t reload_count);

/* core 2.x */
hw_timer_t *cs_timer_comecar_v2(uint8_t num, uint16_t divider, bool countUp);
void timerAlarmWrite(hw_timer_t *timer, uint64_t alarm_value, bool autoreload);
void timerAlarmEnable(hw_timer_t *timer);
void timerAlarmDisable(hw_timer_t *timer);
bool timerAlarmEnabled(hw_timer_t *timer);
uint64_t timerAlarmRead(hw_timer_t *timer);
void timerSetDivider(hw_timer_t *timer, uint16_t divider);
uint16_t timerGetDivider(hw_timer_t *timer);
bool timerStarted(hw_timer_t *timer);

#ifdef __cplusplus
}

inline hw_timer_t *timerBegin(uint32_t frequency) { return cs_timer_comecar(frequency); }
inline hw_timer_t *timerBegin(uint8_t num, uint16_t divider, bool countUp) { return cs_timer_comecar_v2(num, divider, countUp); }
inline void timerAttachInterrupt(hw_timer_t *timer, void (*fn)(void)) { cs_timer_rotina(timer, fn); }
inline void timerAttachInterrupt(hw_timer_t *timer, void (*fn)(void), bool edge)
{
    (void)edge;
    cs_timer_rotina(timer, fn);
}

#endif

#endif
