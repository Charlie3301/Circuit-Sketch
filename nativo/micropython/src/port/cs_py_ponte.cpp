/* cs_py_ponte.cpp -- a placa vista do MicroPython: o core do Arduino do simulador, em funcoes de C.
 * O pino, o barramento e a rede daqui sao os mesmos do programa em C++. */
#include <Arduino.h>
#include <SPI.h>
#include <Wire.h>
#include <SD.h>
#include <LittleFS.h>
#include <Adafruit_NeoPixel.h>
#include <DHT.h>
#if defined(ARDUINO_ARCH_RP2040)
#include "pico/time.h"
#endif
#if defined(ESP32)
#include <WiFi.h>
#include "esp_random.h"
#include "esp_task_wdt.h"
#include "esp_timer.h"
#include "esp_sleep.h"
#include "esp_system.h"
#endif
#include <sys/time.h>
#include <map>
#include "cs_ponte.h"
#include "port/cs_py_ponte.h"

extern "C" {

uint32_t cs_py_ms(void) { return (uint32_t)millis(); }
uint32_t cs_py_us(void) { return (uint32_t)micros(); }
void cs_py_esperar_ms(uint32_t ms) { delay(ms); }
void cs_py_esperar_us(uint32_t us) { delayMicroseconds(us); }
uint32_t cs_py_atomica_comeca(void)
{
    noInterrupts();
    return 0;
}
void cs_py_atomica_termina(uint32_t) { interrupts(); }
#if defined(ESP32)
uint32_t cs_py_aleatorio(void) { return esp_random(); }
#else
uint32_t cs_py_aleatorio(void) { return ((uint32_t)random(0x10000) << 16) | (uint32_t)random(0x10000); }
#endif
void cs_py_ceder(void) { yield(); }
#if defined(ESP32)
void cs_py_reiniciar(void) { ESP.restart(); }
int cs_py_bootsel(void) { return 0; }
#else
void cs_py_reiniciar(void) { rp2040.reboot(); }
int cs_py_bootsel(void) { return BOOTSEL ? 1 : 0; }
#endif

int64_t cs_py_epoca_us(void)
{
    struct timeval t;
    gettimeofday(&t, nullptr);
    return (int64_t)t.tv_sec * 1000000 + t.tv_usec;
}

void cs_py_acertar_epoca_us(int64_t us)
{
    struct timeval t;
    t.tv_sec = (time_t)(us / 1000000);
    t.tv_usec = (long)(us % 1000000);
    settimeofday(&t, nullptr);
}

/* ---- o monitor serial ---- */
void cs_py_serial_escrever(const char *s, size_t n)
{
    static bool ligado = false;
    if (!ligado) {
        Serial.begin(115200);
        ligado = true;
    }
    Serial.write((const uint8_t *)s, n);
}
int cs_py_serial_ler(void) { return Serial.available() > 0 ? Serial.read() : -1; }

/* ---- os pinos ---- */
void cs_py_pino_modo(int p, int modo)
{
    switch (modo) {
    case CS_PY_SAIDA: pinMode(p, OUTPUT); break;
#if defined(OUTPUT_OPEN_DRAIN)
    case CS_PY_DRENO: pinMode(p, OUTPUT_OPEN_DRAIN); break;
#else
    /* A Pico nao tem o dreno aberto no pino: o port rp2 do MicroPython tambem o imita com a saida. */
    case CS_PY_DRENO: pinMode(p, OUTPUT); break;
#endif
    case CS_PY_PULL_UP: pinMode(p, INPUT_PULLUP); break;
    case CS_PY_PULL_DOWN: pinMode(p, INPUT_PULLDOWN); break;
    default: pinMode(p, INPUT); break;
    }
}
void cs_py_pino_escrever(int p, int v) { digitalWrite(p, v ? HIGH : LOW); }
int cs_py_pino_ler(int p) { return digitalRead(p); }
bool cs_py_pino_existe(int p) { return p >= 0 && p < NUM_DIGITAL_PINS; }
int cs_py_quantos_pinos(void) { return NUM_DIGITAL_PINS; }
void cs_py_pino_interrupcao(int p, int borda, cs_py_isr_t f, void *arg)
{
    int modo = borda == (CS_PY_SUBIDA | CS_PY_DESCIDA) ? CHANGE : borda == CS_PY_SUBIDA ? RISING : FALLING;
    attachInterruptArg(digitalPinToInterrupt(p), f, arg, modo);
}
void cs_py_pino_sem_interrupcao(int p) { detachInterrupt(digitalPinToInterrupt(p)); }

/* ---- ADC, PWM, DAC, toque ---- */
int cs_py_adc_ler(int p) { return analogRead(p); }
void cs_py_adc_bits(int bits) { analogReadResolution(bits); }
#if defined(ESP32)
void cs_py_adc_atenuacao(int p, int a) { analogSetPinAttenuation(p, (adc_attenuation_t)a); }
#else
void cs_py_adc_atenuacao(int, int) {}
#endif

#if defined(ESP32)
/* O LEDC tem a resolucao que a frequencia deixa, como na placa (80 MHz de relogio). */
static uint8_t bitsDoPwm(uint32_t freq)
{
    uint8_t bits = 1;
    while (bits < 16 && ((uint64_t)freq << (bits + 1)) <= 80000000ULL) bits++;
    return bits;
}
static std::map<int, uint8_t> &pwmAtivos()
{
    static std::map<int, uint8_t> m;
    return m;
}
void cs_py_pwm_iniciar(int p, uint32_t freq, uint32_t duty16)
{
    if (pwmAtivos().count(p)) ledcDetach(p);
    uint8_t bits = bitsDoPwm(freq);
    ledcAttach(p, freq, bits);
    pwmAtivos()[p] = bits;
    cs_py_pwm_duty(p, duty16);
}
void cs_py_pwm_duty(int p, uint32_t duty16)
{
    auto i = pwmAtivos().find(p);
    if (i != pwmAtivos().end()) ledcWrite(p, ((uint64_t)duty16 * ((1u << i->second) - 1) + 32767) / 65535);
}
void cs_py_pwm_parar(int p)
{
    if (pwmAtivos().erase(p)) ledcDetach(p);
}
#else
/* O PWM da Pico: 16 bits e a frequencia global do analogWrite. */
void cs_py_pwm_iniciar(int p, uint32_t freq, uint32_t duty16)
{
    analogWriteFrequency(freq);
    analogWriteResolution(16);
    analogWrite(p, duty16);
}
void cs_py_pwm_duty(int p, uint32_t duty16) { analogWrite(p, duty16); }
void cs_py_pwm_parar(int p) { analogWrite(p, 0); }
#endif
#if defined(ESP32)
void cs_py_dac_escrever(int p, int v) { dacWrite(p, (uint8_t)v); }
int cs_py_toque_ler(int p) { return touchRead(p); }
#else
void cs_py_dac_escrever(int, int) {}
int cs_py_toque_ler(int) { return 0; }
#endif

/* ---- I2C ---- */
static TwoWire &i2cDe(int bus) { return bus == 0 ? Wire : Wire1; }
void cs_py_i2c_iniciar(int bus, int sda, int scl, uint32_t freq)
{
#if defined(ARDUINO_ARCH_RP2040)
    /* Na Pico os pinos vao antes do begin(), como no core arduino-pico. */
    i2cDe(bus).setSDA(sda);
    i2cDe(bus).setSCL(scl);
    i2cDe(bus).begin();
    i2cDe(bus).setClock(freq);
#else
    i2cDe(bus).begin(sda, scl, freq);
#endif
    i2cDe(bus).setBufferSize(4096);
}
void cs_py_i2c_parar(int bus) { i2cDe(bus).end(); }
int cs_py_i2c_escrever(int bus, uint16_t endereco, const uint8_t *dados, size_t n, bool parar)
{
    TwoWire &w = i2cDe(bus);
    w.beginTransmission((uint8_t)endereco);
    if (n) w.write(dados, n);
    return w.endTransmission(parar);
}
int cs_py_i2c_ler(int bus, uint16_t endereco, uint8_t *dados, size_t n, bool parar)
{
    TwoWire &w = i2cDe(bus);
    size_t veio = w.requestFrom((uint8_t)endereco, n, parar);
    for (size_t i = 0; i < veio && i < n; i++) dados[i] = (uint8_t)w.read();
    return (int)veio;
}

/* ---- SPI: na ESP32 o 2 e o VSPI (o SPI do Arduino) e o 1 e o HSPI; na Pico, spi0 e spi1 ---- */
static SPIClass &spiDe(int bus)
{
#if defined(ESP32)
    return bus == 1 ? cs_segundo_spi : SPI;
#else
    return bus == 1 ? SPI1 : SPI;
#endif
}
void cs_py_spi_iniciar(int bus, int sck, int mosi, int miso, uint32_t freq, int polaridade, int fase, int primeiro)
{
    SPIClass &s = spiDe(bus);
#if defined(ARDUINO_ARCH_RP2040)
    s.setSCK(sck);
    s.setTX(mosi);
    s.setRX(miso);
    s.begin();
#else
    s.begin(sck, miso, mosi);
#endif
    s.setFrequency(freq);
    s.setDataMode((polaridade ? 2 : 0) | (fase ? 1 : 0));
    s.setBitOrder(primeiro ? LSBFIRST : MSBFIRST);
}
void cs_py_spi_parar(int bus) { spiDe(bus).end(); }
void cs_py_spi_trocar(int bus, size_t n, const uint8_t *saida, uint8_t *entrada)
{
    SPIClass &s = spiDe(bus);
    for (size_t i = 0; i < n; i++) {
        uint8_t v = s.transfer(saida ? saida[i] : 0xFF);
        if (entrada) entrada[i] = v;
    }
}

/* ---- UART ---- */
static HardwareSerial &uartDe(int u)
{
#if !defined(ESP32) || SOC_UART_NUM > 2
    if (u == 2) return Serial2;
#endif
    return u == 1 ? Serial1 : Serial;
}
void cs_py_uart_iniciar(int u, uint32_t baud, int bits, int paridade, int parada, int tx, int rx, int rxbuf)
{
    /* O formato do SERIAL_8N1 da ESP32. */
    uint32_t cfg = 0x8000000u | (uint32_t)((bits - 5) << 2) | (parada == 2 ? 0x30u : 0x10u) | (paridade == 0 ? 2u : paridade == 1 ? 3u : 0u);
    /* A UART do MicroPython 0 e a Serial1 da Pico: a Serial e a USB (o REPL). */
#if defined(ARDUINO_ARCH_RP2040)
    u = u + 1;
#endif
    HardwareSerial &s = uartDe(u);
    if (rxbuf > 0) s.setRxBufferSize((size_t)rxbuf);
#if defined(ARDUINO_ARCH_RP2040)
    /* Os pinos da UART da Pico sao os de fabrica do MicroPython (0/1 e 4/5): o begin() do core. */
    (void)tx;
    (void)rx;
    s.begin(baud, cfg);
#else
    s.begin(baud, cfg, rx, tx);
#endif
}
void cs_py_uart_parar(int u) { uartDe(u).end(); }
size_t cs_py_uart_escrever(int u, const uint8_t *d, size_t n) { return uartDe(u).write(d, n); }
int cs_py_uart_disponivel(int u) { return uartDe(u).available(); }
int cs_py_uart_ler(int u) { return uartDe(u).read(); }
void cs_py_uart_quebra(int u) { uartDe(u).flush(); }

/* ---- a fita e o DHT ---- */
void cs_py_fita_escrever(int p, const uint8_t *b, size_t n)
{
    static std::map<int, Adafruit_NeoPixel *> fitas;
    size_t leds = n / 3;
    Adafruit_NeoPixel *&f = fitas[p];
    if (!f || f->numPixels() != leds) {
        f = new Adafruit_NeoPixel((uint16_t)leds, (int16_t)p, NEO_GRB + NEO_KHZ800);
        f->begin();
    }
    /* Os bytes como vao no fio: verde, vermelho, azul. */
    for (size_t i = 0; i < leds; i++) f->setPixelColor((uint16_t)i, b[3 * i + 1], b[3 * i], b[3 * i + 2]);
    f->show();
}

bool cs_py_dht_medir(int p, int tipo, float *temperatura, float *umidade)
{
    static std::map<int, DHT *> sensores;
    DHT *&d = sensores[p];
    if (!d) {
        d = new DHT((uint8_t)p, (uint8_t)tipo);
        d->begin();
    }
    float t = d->readTemperature(), h = d->readHumidity();
    if (isnan(t) || isnan(h)) return false;
    *temperatura = t;
    *umidade = h;
    return true;
}

#if defined(ESP32)
/* ---- o radio BLE do bluetooth.BLE ---- */
int cs_py_ble(const char *op, const char *texto, long a, long b, long c, char *volta, int cabe)
{
    String r = cs_obj_t(nullptr, "__mpble", op, texto, (int)a, (int)b, (int)c);
    int n = (int)r.length();
    if (n >= cabe) n = cabe - 1;
    memcpy(volta, r.c_str(), n);
    volta[n] = 0;
    return n;
}

/* ---- o Timer ---- */
struct TimerPy {
    esp_timer_handle_t h;
    bool rodando;
};
void *cs_py_timer_novo(cs_py_timer_t f, void *arg)
{
    auto *t = new TimerPy{};
    esp_timer_create_args_t a = {};
    a.callback = f;
    a.arg = arg;
    a.name = "machine.Timer";
    esp_timer_create(&a, &t->h);
    return t;
}
void cs_py_timer_iniciar(void *p, uint64_t us, bool repete)
{
    auto *t = (TimerPy *)p;
    if (t->rodando) esp_timer_stop(t->h);
    if (repete) esp_timer_start_periodic(t->h, us);
    else esp_timer_start_once(t->h, us);
    t->rodando = true;
}
void cs_py_timer_parar(void *p)
{
    auto *t = (TimerPy *)p;
    if (t->rodando) esp_timer_stop(t->h);
    t->rodando = false;
}
void cs_py_timer_apagar(void *p)
{
    auto *t = (TimerPy *)p;
    cs_py_timer_parar(t);
    esp_timer_delete(t->h);
    delete t;
}

/* ---- a rede: 0 e o STA, 1 e o AP, como no MicroPython ---- */
static bool wifiStaLigado, wifiApLigado;
static void aplicarModo()
{
    WiFi.mode(wifiStaLigado && wifiApLigado ? WIFI_AP_STA : wifiStaLigado ? WIFI_STA : wifiApLigado ? WIFI_AP : WIFI_OFF);
}
bool cs_py_wifi_ativar(int i, bool ligado)
{
    (i == 0 ? wifiStaLigado : wifiApLigado) = ligado;
    aplicarModo();
    return ligado;
}
bool cs_py_wifi_ativo(int i) { return i == 0 ? wifiStaLigado : wifiApLigado; }
void cs_py_wifi_conectar(const char *ssid, const char *senha) { WiFi.begin(ssid, senha); }
void cs_py_wifi_desconectar(void) { WiFi.disconnect(); }
int cs_py_wifi_status(void) { return (int)WiFi.status(); }
bool cs_py_wifi_conectado(void) { return WiFi.status() == WL_CONNECTED; }
void cs_py_wifi_criar_ap(const char *ssid, const char *senha) { WiFi.softAP(ssid, senha && *senha ? senha : nullptr); }
int cs_py_wifi_clientes_do_ap(void) { return WiFi.softAPgetStationNum(); }
static void copiarIp(char *d, IPAddress ip) { snprintf(d, 16, "%u.%u.%u.%u", ip[0], ip[1], ip[2], ip[3]); }
void cs_py_wifi_ip(int i, char ip[16], char mascara[16], char porta[16], char dns[16])
{
    if (i == 0) {
        copiarIp(ip, WiFi.localIP());
        copiarIp(mascara, WiFi.subnetMask());
        copiarIp(porta, WiFi.gatewayIP());
        copiarIp(dns, WiFi.dnsIP());
    } else {
        copiarIp(ip, WiFi.softAPIP());
        copiarIp(mascara, WiFi.softAPSubnetMask());
        copiarIp(porta, WiFi.softAPIP());
        copiarIp(dns, WiFi.softAPIP());
    }
}
void cs_py_wifi_definir_ip(int i, const char *ip, const char *mascara, const char *porta, const char *dns)
{
    IPAddress a, b, c, d;
    a.fromString(ip);
    b.fromString(mascara);
    c.fromString(porta);
    d.fromString(dns);
    if (i == 0) WiFi.config(a, c, b, d);
    else WiFi.softAPConfig(a, c, b);
}
void cs_py_wifi_mac(int i, uint8_t mac[6])
{
    if (i == 0) WiFi.macAddress(mac);
    else WiFi.softAPmacAddress(mac);
}
int cs_py_wifi_rssi(void) { return WiFi.RSSI(); }
int cs_py_wifi_varrer(void) { return WiFi.scanNetworks(); }
void cs_py_wifi_rede(int i, const char **ssid, uint8_t bssid[6], int *canal, int *rssi, int *seguranca)
{
    static String nome;
    nome = WiFi.SSID(i);
    *ssid = nome.c_str();
    memset(bssid, 0, 6);
    WiFi.BSSID((uint8_t)i, bssid);
    *canal = WiFi.channel(i);
    *rssi = WiFi.RSSI(i);
    *seguranca = (int)WiFi.encryptionType(i);
}
const char *cs_py_wifi_nome(void) { return WiFi.getHostname(); }
void cs_py_wifi_definir_nome(const char *nome) { WiFi.setHostname(nome); }

/* ---- o cao de guarda: o WDT de tarefa, como o port da ESP32 ---- */
void cs_py_wdt_iniciar(uint32_t ms)
{
    esp_task_wdt_config_t c = {};
    c.timeout_ms = ms;
    c.trigger_panic = true;
    esp_task_wdt_reconfigure(&c);
    esp_task_wdt_add(nullptr);
}
void cs_py_wdt_alimentar(void) { esp_task_wdt_reset(); }

/* ---- o sono ---- */
void cs_py_dormir_fundo(uint64_t us)
{
    if (us) esp_sleep_enable_timer_wakeup(us);
    esp_deep_sleep_start();
}
void cs_py_dormir_leve(uint64_t us)
{
    if (us) esp_sleep_enable_timer_wakeup(us);
    esp_light_sleep_start();
}
int cs_py_motivo_do_reinicio(void) { return (int)esp_reset_reason(); }

#else
/* O Timer da Pico: o add_repeating_timer do SDK, que o core ja simula. */
struct TimerPy {
    cs_py_timer_t f;
    void *arg;
    repeating_timer t;
    bool rodando, repete;
};
static bool aoEsgotarNaPico(repeating_timer *r)
{
    auto *t = (TimerPy *)r->user_data;
    t->f(t->arg);
    if (!t->repete) t->rodando = false;
    return t->repete;
}
void *cs_py_timer_novo(cs_py_timer_t f, void *arg) { return new TimerPy{f, arg}; }
void cs_py_timer_parar(void *p)
{
    auto *t = (TimerPy *)p;
    if (t->rodando) cancel_repeating_timer(&t->t);
    t->rodando = false;
}
void cs_py_timer_iniciar(void *p, uint64_t us, bool repete)
{
    auto *t = (TimerPy *)p;
    cs_py_timer_parar(t);
    t->repete = repete;
    t->rodando = add_repeating_timer_us(-(int64_t)us, aoEsgotarNaPico, t, &t->t);
}
void cs_py_timer_apagar(void *p)
{
    cs_py_timer_parar(p);
    delete (TimerPy *)p;
}

void cs_py_wdt_iniciar(uint32_t) {}
void cs_py_wdt_alimentar(void) {}
void cs_py_dormir_fundo(uint64_t us) { delay((uint32_t)(us / 1000)); rp2040.reboot(); }
void cs_py_dormir_leve(uint64_t us) { delay((uint32_t)(us / 1000)); }
int cs_py_motivo_do_reinicio(void) { return 1; }
void cs_py_wifi_mac(int, uint8_t mac[6])
{
    static const uint8_t fixo[6] = {0xE6, 0x61, 0x38, 0x1A, 0x2B, 0x3C};
    memcpy(mac, fixo, 6);
}
#endif

/* ---- os arquivos: "/sd/..." e o cartao, o resto e a flash ---- */
static bool cartaoMontado;
static bool noCartao(const char *c) { return cartaoMontado && (!strcmp(c, "/sd") || !strncmp(c, "/sd/", 4)); }
static fs::FS &fsDe(const char *c) { return noCartao(c) ? (fs::FS &)SD : (fs::FS &)LittleFS; }
static String dentro(const char *c)
{
    String s = noCartao(c) ? String(c + 3) : String(c);
    return s.length() ? s : String("/");
}
static void flashPronta()
{
    static bool pronta = false;
    if (!pronta) pronta = LittleFS.begin(true);
}

bool cs_py_cartao_montar(void) { return cartaoMontado = SD.begin(); }
void cs_py_cartao_desmontar(void)
{
    SD.end();
    cartaoMontado = false;
}

void *cs_py_arquivo_abrir(const char *caminho, const char *modo)
{
    flashPronta();
    File f = fsDe(caminho).open(dentro(caminho).c_str(), modo, *modo != 'r');
    if (!f || f.isDirectory()) return nullptr;
    return new File(f);
}
void cs_py_arquivo_fechar(void *a)
{
    File *f = (File *)a;
    f->close();
    delete f;
}
int cs_py_arquivo_ler(void *a, uint8_t *d, size_t n) { return ((File *)a)->read(d, n); }
int cs_py_arquivo_escrever(void *a, const uint8_t *o, size_t n) { return (int)((File *)a)->write(o, n); }
bool cs_py_arquivo_ir(void *a, int64_t p, int origem) { return ((File *)a)->seek((uint32_t)p, origem); }
int64_t cs_py_arquivo_posicao(void *a) { return (int64_t)((File *)a)->position(); }
void cs_py_arquivo_salvar(void *a) { ((File *)a)->flush(); }
bool cs_py_arq_existe(const char *c)
{
    flashPronta();
    return !strcmp(c, "/") || fsDe(c).exists(dentro(c).c_str());
}
bool cs_py_arq_eh_pasta(const char *c)
{
    flashPronta();
    if (!strcmp(c, "/")) return true;
    File f = fsDe(c).open(dentro(c).c_str(), "r");
    bool sim = f && f.isDirectory();
    if (f) f.close();
    return sim;
}
int64_t cs_py_arq_tamanho(const char *c)
{
    flashPronta();
    File f = fsDe(c).open(dentro(c).c_str(), "r");
    int64_t n = f ? (int64_t)f.size() : -1;
    if (f) f.close();
    return n;
}
bool cs_py_arq_apagar(const char *c)
{
    flashPronta();
    return fsDe(c).remove(dentro(c).c_str());
}
bool cs_py_arq_renomear(const char *de, const char *para)
{
    flashPronta();
    return fsDe(de).rename(dentro(de).c_str(), dentro(para).c_str());
}
bool cs_py_arq_criar_pasta(const char *c)
{
    flashPronta();
    return fsDe(c).mkdir(dentro(c).c_str());
}
bool cs_py_arq_apagar_pasta(const char *c)
{
    flashPronta();
    return fsDe(c).rmdir(dentro(c).c_str());
}
void *cs_py_pasta_abrir(const char *c)
{
    flashPronta();
    File f = fsDe(c).open(dentro(c).c_str(), "r");
    if (!f || !f.isDirectory()) return nullptr;
    return new File(f);
}
bool cs_py_pasta_proximo(void *p, char *nome, size_t max, bool *pasta, int64_t *tamanho)
{
    File f = ((File *)p)->openNextFile();
    if (!f) return false;
    const char *n = f.name();
    const char *barra = strrchr(n, '/');
    snprintf(nome, max, "%s", barra ? barra + 1 : n);
    *pasta = f.isDirectory();
    *tamanho = *pasta ? 0 : (int64_t)f.size();
    f.close();
    return true;
}
void cs_py_pasta_fechar(void *p)
{
    File *f = (File *)p;
    f->close();
    delete f;
}

/* ---- o erro que escapou do programa ---- */
void cs_py_erro_do_programa(const char *tipo, const char *mensagem, const char *arquivo, int linha)
{
    cs_val a[] = {cs_t(tipo), cs_t(mensagem), cs_t(arquivo), cs_i(linha)};
    cs_nada(nullptr, "__erro_python", 4, a);
}

} // extern "C"
