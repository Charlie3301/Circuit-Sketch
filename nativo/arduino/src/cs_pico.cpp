/* cs_pico.cpp -- o SDK da Pico sobre o core (ver cs_pico.h). */
#include "cs_pico.h"
#include "cs_interno.h"
#include "freertos/FreeRTOS.h"

/* ---- o stdio: o printf sai no monitor (a USB da placa) ---- */
extern "C" bool stdio_init_all(void)
{
    Serial.begin(115200);
    return true;
}
extern "C" bool stdio_usb_init(void) { return stdio_init_all(); }
extern "C" void stdio_uart_init(void) { stdio_init_all(); }
extern "C" bool stdio_usb_connected(void) { return true; }
extern "C" void stdio_flush(void) { Serial.flush(); }

extern "C" int getchar_timeout_us(uint32_t us)
{
    uint64_t fim = time_us_64() + us;
    while (!Serial.available()) {
        if (time_us_64() >= fim) return PICO_ERROR_TIMEOUT;
        delay(1);
    }
    return Serial.read();
}

/* ---- os pinos: pelo nome, no InterpretadorPicoSdk ---- */
extern "C" void gpio_init(uint g) { ch(nullptr, "gpio_init", cs_i(g)); }
extern "C" void gpio_deinit(uint g) { ch(nullptr, "gpio_deinit", cs_i(g)); }
extern "C" void gpio_init_mask(uint32_t m)
{
    for (uint g = 0; g < 32; g++)
        if (m & (1u << g)) gpio_init(g);
}
extern "C" void gpio_set_dir(uint g, bool saida) { ch(nullptr, "gpio_set_dir", cs_i(g), cs_i(saida ? 1 : 0)); }
extern "C" void gpio_set_dir_masked(uint32_t m, uint32_t v)
{
    for (uint g = 0; g < 32; g++)
        if (m & (1u << g)) gpio_set_dir(g, (v >> g) & 1);
}
extern "C" void gpio_set_dir_out_masked(uint32_t m) { gpio_set_dir_masked(m, 0xFFFFFFFFu); }
extern "C" void gpio_set_dir_in_masked(uint32_t m) { gpio_set_dir_masked(m, 0); }
extern "C" void gpio_set_dir_all_bits(uint32_t v) { gpio_set_dir_masked(0x3FFFFFFFu, v); }
extern "C" void gpio_put(uint g, bool v) { ch(nullptr, "gpio_put", cs_i(g), cs_i(v ? 1 : 0)); }
extern "C" bool gpio_get(uint g) { return ch_i(nullptr, "gpio_get", cs_i(g)) != 0; }
extern "C" bool gpio_get_out_level(uint g) { return gpio_get(g); }
extern "C" uint32_t gpio_get_all(void)
{
    uint32_t v = 0;
    for (uint g = 0; g < 30; g++)
        if (gpio_get(g)) v |= 1u << g;
    return v;
}
extern "C" void gpio_put_masked(uint32_t m, uint32_t v)
{
    for (uint g = 0; g < 32; g++)
        if (m & (1u << g)) gpio_put(g, (v >> g) & 1);
}
extern "C" void gpio_put_all(uint32_t v) { gpio_put_masked(0x3FFFFFFFu, v); }
extern "C" void gpio_set_mask(uint32_t m) { ch(nullptr, "gpio_set_mask", cs_i(m)); }
extern "C" void gpio_clr_mask(uint32_t m) { ch(nullptr, "gpio_clr_mask", cs_i(m)); }
extern "C" void gpio_xor_mask(uint32_t m) { ch(nullptr, "gpio_xor_mask", cs_i(m)); }

static uint8_t puxada[32];          /* 1 para cima, 2 para baixo */
extern "C" void gpio_pull_up(uint g)
{
    if (g < 32) puxada[g] = 1;
    ch(nullptr, "gpio_pull_up", cs_i(g));
}
extern "C" void gpio_pull_down(uint g)
{
    if (g < 32) puxada[g] = 2;
    ch(nullptr, "gpio_pull_down", cs_i(g));
}
extern "C" void gpio_disable_pulls(uint g)
{
    if (g < 32) puxada[g] = 0;
    ch(nullptr, "gpio_disable_pulls", cs_i(g));
}
extern "C" void gpio_set_pulls(uint g, bool cima, bool baixo)
{
    if (cima) gpio_pull_up(g);
    else if (baixo) gpio_pull_down(g);
    else gpio_disable_pulls(g);
}
extern "C" bool gpio_is_pulled_up(uint g) { return g < 32 && puxada[g] == 1; }
extern "C" bool gpio_is_pulled_down(uint g) { return g < 32 && puxada[g] == 2; }

static uint8_t funcao[32];
extern "C" void gpio_set_function(uint g, enum gpio_function fn)
{
    if (g < 32) funcao[g] = (uint8_t)fn;
    ch(nullptr, "gpio_set_function", cs_i(g), cs_i((int)fn));
}
extern "C" enum gpio_function gpio_get_function(uint g) { return g < 32 ? (enum gpio_function)funcao[g] : GPIO_FUNC_NULL; }
extern "C" void gpio_set_input_enabled(uint g, bool e) { ch(nullptr, "gpio_set_input_enabled", cs_i(g), cs_i(e)); }
extern "C" bool gpio_is_dir_out(uint) { return false; }

/* A interrupcao do pino: um callback so para todos, com o pino e a borda. */
static gpio_irq_callback_t callbackDosPinos;
static uint32_t bordasDoPino[32];

static void interrupcaoDoPino(void *arg)
{
    uint g = (uint)(uintptr_t)arg;
    if (!callbackDosPinos || g >= 32) return;
    uint32_t querido = bordasDoPino[g];
    uint32_t evento = 0;
    bool nivel = digitalRead(g) != 0;
    if (querido & (GPIO_IRQ_EDGE_RISE | GPIO_IRQ_EDGE_FALL))
        evento = nivel ? GPIO_IRQ_EDGE_RISE : GPIO_IRQ_EDGE_FALL;
    else
        evento = nivel ? GPIO_IRQ_LEVEL_HIGH : GPIO_IRQ_LEVEL_LOW;
    if (evento & querido) callbackDosPinos(g, evento);
}

extern "C" void gpio_set_irq_callback(gpio_irq_callback_t cb) { callbackDosPinos = cb; }

extern "C" void gpio_set_irq_enabled(uint g, uint32_t eventos, bool ligar)
{
    if (g >= 32) return;
    if (!ligar) {
        bordasDoPino[g] &= ~eventos;
        if (!bordasDoPino[g]) detachInterrupt(g);
        return;
    }
    bordasDoPino[g] |= eventos;
    uint32_t e = bordasDoPino[g];
    int modo = (e & GPIO_IRQ_EDGE_RISE) && (e & GPIO_IRQ_EDGE_FALL) ? CHANGE
               : (e & GPIO_IRQ_EDGE_RISE) ? RISING
               : (e & GPIO_IRQ_EDGE_FALL) ? FALLING
               : (e & GPIO_IRQ_LEVEL_LOW) ? LOW : HIGH;
    attachInterruptArg((uint8_t)g, interrupcaoDoPino, (void *)(uintptr_t)g, modo);
}

extern "C" void gpio_set_irq_enabled_with_callback(uint g, uint32_t eventos, bool ligar, gpio_irq_callback_t cb)
{
    if (cb) callbackDosPinos = cb;
    gpio_set_irq_enabled(g, eventos, ligar);
}
extern "C" void gpio_acknowledge_irq(uint, uint32_t) {}

/* ---- o tempo (o relogio da placa, e o sleep que cede como o delay) ---- */
extern "C" void sleep_ms(uint32_t ms) { delay(ms); }
extern "C" void sleep_us(uint64_t us)
{
    if (us >= 1000) delay((unsigned long)(us / 1000));
    if (us % 1000) delayMicroseconds((unsigned int)(us % 1000));
}
extern "C" uint64_t time_us_64(void) { return (uint64_t)cs_relogio(); }
extern "C" uint32_t time_us_32(void) { return (uint32_t)time_us_64(); }
extern "C" absolute_time_t get_absolute_time(void) { return time_us_64(); }
extern "C" void sleep_until(absolute_time_t t)
{
    uint64_t agora = time_us_64();
    if (t > agora) sleep_us(t - agora);
}
extern "C" void busy_wait_us(uint64_t us) { sleep_us(us); }
extern "C" void busy_wait_us_32(uint32_t us) { sleep_us(us); }
extern "C" void busy_wait_ms(uint32_t ms) { delay(ms); }
extern "C" uint32_t to_ms_since_boot(absolute_time_t t) { return (uint32_t)(t / 1000); }
extern "C" uint64_t to_us_since_boot(absolute_time_t t) { return t; }
extern "C" int64_t absolute_time_diff_us(absolute_time_t de, absolute_time_t ate) { return (int64_t)(ate - de); }
extern "C" absolute_time_t delayed_by_ms(absolute_time_t t, uint32_t ms) { return t + (uint64_t)ms * 1000; }
extern "C" absolute_time_t delayed_by_us(absolute_time_t t, uint64_t us) { return t + us; }
extern "C" absolute_time_t make_timeout_time_ms(uint32_t ms) { return time_us_64() + (uint64_t)ms * 1000; }
extern "C" absolute_time_t make_timeout_time_us(uint64_t us) { return time_us_64() + us; }
extern "C" bool time_reached(absolute_time_t t) { return time_us_64() >= t; }
extern "C" bool is_nil_time(absolute_time_t t) { return t == 0; }
extern "C" void tight_loop_contents(void) {}

/* Os alarmes e os temporizadores que repetem: os do FreeRTOS (a placa os
 * chama na interrupcao do timer; aqui, na tarefa dos temporizadores). */
struct Alarme {
    alarm_callback_t alarme;
    struct repeating_timer *repete;
    void *dados;
    int32_t id;
    TimerHandle_t t;
};
static int32_t proximoAlarme = 1;

static void dispararAlarme(TimerHandle_t t)
{
    auto *a = (Alarme *)pvTimerGetTimerID(t);
    if (a->alarme) {
        int64_t r = a->alarme(a->id, a->dados);
        if (r == 0) return;
        int64_t us = r < 0 ? -r : r;
        xTimerChangePeriod(t, us / 1000 ? (TickType_t)(us / 1000) : 1, 0);
        return;
    }
    if (a->repete && !a->repete->callback(a->repete)) xTimerStop(t, 0);
}

static Alarme *novoAlarme(uint64_t us, bool repete)
{
    auto *a = new Alarme();
    a->id = proximoAlarme++;
    a->t = xTimerCreate("alarm", us / 1000 ? (TickType_t)(us / 1000) : 1, repete ? pdTRUE : pdFALSE, a, dispararAlarme);
    return a;
}

static Alarme *alarmes[64];

extern "C" alarm_id_t add_alarm_in_us(uint64_t us, alarm_callback_t cb, void *dados, bool)
{
    Alarme *a = novoAlarme(us, false);
    a->alarme = cb;
    a->dados = dados;
    alarmes[a->id & 63] = a;
    xTimerStart(a->t, 0);
    return a->id;
}
extern "C" alarm_id_t add_alarm_in_ms(uint32_t ms, alarm_callback_t cb, void *dados, bool passado)
{
    return add_alarm_in_us((uint64_t)ms * 1000, cb, dados, passado);
}
extern "C" bool cancel_alarm(alarm_id_t id)
{
    Alarme *a = alarmes[id & 63];
    if (!a || a->id != id) return false;
    xTimerStop(a->t, 0);
    return true;
}

extern "C" bool add_repeating_timer_us(int64_t us, repeating_timer_callback_t cb, void *dados, struct repeating_timer *saida)
{
    if (!saida) return false;
    Alarme *a = novoAlarme((uint64_t)(us < 0 ? -us : us), true);
    saida->delay_us = us;
    saida->callback = cb;
    saida->user_data = dados;
    saida->alarm_id = a->id;
    a->repete = saida;
    alarmes[a->id & 63] = a;
    xTimerStart(a->t, 0);
    return true;
}
extern "C" bool add_repeating_timer_ms(int32_t ms, repeating_timer_callback_t cb, void *dados, struct repeating_timer *saida)
{
    return add_repeating_timer_us((int64_t)ms * 1000, cb, dados, saida);
}
extern "C" bool cancel_repeating_timer(struct repeating_timer *r) { return r && cancel_alarm(r->alarm_id); }

/* ---- o ADC ---- */
extern "C" void adc_init(void) { ch(nullptr, "adc_init"); }
extern "C" void adc_gpio_init(uint g) { ch(nullptr, "adc_gpio_init", cs_i(g)); }
extern "C" void adc_select_input(uint i) { ch(nullptr, "adc_select_input", cs_i(i)); }
extern "C" uint adc_get_selected_input(void) { return (uint)ch_i(nullptr, "adc_get_selected_input"); }
extern "C" uint16_t adc_read(void) { return (uint16_t)ch_i(nullptr, "adc_read"); }
extern "C" void adc_set_temp_sensor_enabled(bool e) { ch(nullptr, "adc_set_temp_sensor_enabled", cs_i(e)); }
extern "C" void adc_set_round_robin(uint) {}

/* ---- o PWM: a configuracao e daqui; a fatia, do simulador ---- */
extern "C" uint pwm_gpio_to_slice_num(uint g) { return (g >> 1) & 7; }
extern "C" uint pwm_gpio_to_channel(uint g) { return g & 1; }
extern "C" pwm_config pwm_get_default_config(void)
{
    pwm_config c = {0, 16, 0xFFFF};
    return c;
}
extern "C" void pwm_config_set_wrap(pwm_config *c, uint16_t w) { c->top = w; }
extern "C" void pwm_config_set_clkdiv(pwm_config *c, float d) { c->div = (uint32_t)(d < 1 ? 16 : d * 16 + 0.5f); }
extern "C" void pwm_config_set_clkdiv_int(pwm_config *c, uint d) { c->div = (d ? d : 1) * 16; }
extern "C" void pwm_config_set_clkdiv_int_frac(pwm_config *c, uint8_t i, uint8_t f) { c->div = (uint32_t)(i ? i : 1) * 16 + (f & 15); }
extern "C" void pwm_config_set_phase_correct(pwm_config *c, bool) { (void)c; }
extern "C" void pwm_config_set_output_polarity(pwm_config *c, bool, bool) { (void)c; }
extern "C" void pwm_config_set_clkdiv_mode(pwm_config *c, int) { (void)c; }
extern "C" void pwm_set_wrap(uint s, uint16_t w) { ch(nullptr, "pwm_set_wrap", cs_i(s), cs_i(w)); }
extern "C" void pwm_set_clkdiv(uint s, float d) { ch(nullptr, "pwm_set_clkdiv", cs_i(s), cs_r(d)); }
extern "C" void pwm_set_clkdiv_int_frac(uint s, uint8_t i, uint8_t f) { ch(nullptr, "pwm_set_clkdiv_int_frac", cs_i(s), cs_i(i), cs_i(f)); }
extern "C" void pwm_set_enabled(uint s, bool e) { ch(nullptr, "pwm_set_enabled", cs_i(s), cs_i(e)); }
extern "C" void pwm_set_mask_enabled(uint32_t m) { ch(nullptr, "pwm_set_mask_enabled", cs_i(m)); }
extern "C" void pwm_init(uint s, pwm_config *c, bool ligar)
{
    pwm_set_wrap(s, (uint16_t)c->top);
    pwm_set_clkdiv(s, c->div / 16.0f);
    pwm_set_enabled(s, ligar);
}
extern "C" void pwm_set_chan_level(uint s, uint canal, uint16_t nivel)
{
    ch(nullptr, "pwm_set_chan_level", cs_i(s), cs_i(canal), cs_i(nivel));
}
extern "C" void pwm_set_both_levels(uint s, uint16_t a, uint16_t b)
{
    pwm_set_chan_level(s, 0, a);
    pwm_set_chan_level(s, 1, b);
}
extern "C" void pwm_set_gpio_level(uint g, uint16_t nivel) { ch(nullptr, "pwm_set_gpio_level", cs_i(g), cs_i(nivel)); }
extern "C" void pwm_set_phase_correct(uint, bool) {}

/* ---- os relogios ---- */
extern "C" uint32_t clock_get_hz(enum clock_index c)
{
    switch (c) {
    case clk_usb:
    case clk_adc: return 48000000;
    case clk_ref: return 12000000;
    case clk_rtc: return 46875;
    default: return 125000000;
    }
}
extern "C" bool set_sys_clock_khz(uint32_t, bool) { return true; }

/* ---- o objeto rp2040 e o BOOTSEL do core arduino-pico (RP2040Support.h, Bootsel.h) ---- */
#if defined(ARDUINO_ARCH_RP2040)
#include "RP2040Support.h"
#include "Bootsel.h"
#include "cs_objeto.h"
extern "C" volatile bool __otherCoreIdled = false;
RP2040 rp2040;
__Bootsel BOOTSEL;
__Bootsel::operator bool() { return rp2040.getBootselButton(); }
int RP2040::f_cpu() { return (int)cs_obj_i(nullptr, "__rp2040", "f_cpu"); }
int RP2040::getFreeHeap() { return (int)cs_obj_i(nullptr, "__rp2040", "getFreeHeap"); }
int RP2040::getUsedHeap() { return (int)cs_obj_i(nullptr, "__rp2040", "getUsedHeap"); }
int RP2040::getTotalHeap() { return (int)cs_obj_i(nullptr, "__rp2040", "getTotalHeap"); }
void RP2040::reboot() { cs_obj_n(nullptr, "__rp2040", "reboot"); }
void RP2040::wdt_begin(uint32_t delay_ms) { cs_obj_n(nullptr, "__rp2040", "wdt_begin", delay_ms); }
void RP2040::wdt_reset() { cs_obj_n(nullptr, "__rp2040", "wdt_reset"); }
bool RP2040::getBootselButton() { return cs_obj_i(nullptr, "__rp2040", "getBootselButton") != 0; }
bool RP2040::isPicoW() { return cs_obj_i(nullptr, "__rp2040", "isPicoW") != 0; }
#endif
