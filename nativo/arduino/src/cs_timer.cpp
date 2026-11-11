/* cs_timer.cpp -- os registradores de GPIO e o timer de hardware da ESP32
 * (ver cs_registradores.h).
 *
 * A interrupcao do timer roda em lote: a cada volta do vigia (ou do delay
 * do simulador), todas as que venceram desde a anterior, uma depois da
 * outra, cada uma com o tempo dela (tempoDaIsr). Os pinos que ela chaveia
 * guardam as bordas nesse tempo, e o detector de tom transforma a
 * sequencia na frequencia que o buzzer toca. */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_interno.h"
#include "cs_registradores.h"
#include "cs_objeto.h"
#include "soc/spi_reg.h"

/* ---- os pinos pelos registradores ---- */
static uint64_t saida;          /* o nivel que o registrador de saida guarda */
static uint64_t habilitado;     /* o GPIO_ENABLE: os que sao saida */
static int64_t tempoDaIsr = -1; /* o instante da interrupcao do lote (us) */

/* O detector de tom: as subidas recentes de cada pino. */
const int Subidas = 256;
struct Tom {
    int64_t subida[Subidas];
    int quantas;            /* no anel */
    int proxima;
    int hz;                 /* o tom avisado ao simulador (0 nenhum) */
    int64_t ultimaBorda;
};
static Tom tons[64];
static uint64_t comBordas;      /* os pinos com borda recente (para o detector olhar) */

static int64_t agoraUs(void) { return tempoDaIsr >= 0 ? tempoDaIsr : cs_relogio(); }

/* O pino que o registrador mudou: a borda para o detector; o pino que nao
 * esta tocando tom vai ao simulador na hora (o LED aceso pelo registrador). */
static void mudarPino(int p, int nivel)
{
    int64_t t = agoraUs();
    Tom &tom = tons[p];
    tom.ultimaBorda = t;
    comBordas |= 1ULL << p;
    if (nivel) {
        tom.subida[tom.proxima] = t;
        tom.proxima = (tom.proxima + 1) % Subidas;
        if (tom.quantas < Subidas) tom.quantas++;
    }
    if (!tom.hz && tempoDaIsr < 0) ch(nullptr, "digitalWrite", cs_i(p), cs_i(nivel));
}

/* A frequencia de cada pino nos ultimos 30 ms; o que mudou vai ao simulador. */
static void avaliarTons(int64_t agora)
{
    const int64_t janela = 30000;
    for (int p = 0; p < 64; p++) {
        if (!(comBordas & (1ULL << p))) continue;
        Tom &tom = tons[p];
        int n = 0;
        int64_t primeira = 0, ultima = 0;
        for (int k = 0; k < tom.quantas; k++) {
            int64_t t = tom.subida[(tom.proxima - 1 - k + Subidas) % Subidas];
            if (agora - t > janela) break;
            if (n == 0) ultima = t;
            primeira = t;
            n++;
        }
        int hz = n >= 4 && ultima > primeira ? (int)((n - 1) * 1e6 / (double)(ultima - primeira) + 0.5) : 0;
        if (hz < 20) hz = 0;
        if (hz && (!tom.hz || abs(hz - tom.hz) * 100 > tom.hz * 2)) {
            tom.hz = hz;
            ch(nullptr, "__tom", cs_i(p), cs_i(hz));
        } else if (!hz && tom.hz) {
            tom.hz = 0;
            ch(nullptr, "__tom", cs_i(p), cs_i(0));
            ch(nullptr, "digitalWrite", cs_i(p), cs_i((int)((saida >> p) & 1)));
        } else if (!hz && agora - tom.ultimaBorda > janela) {
            comBordas &= ~(1ULL << p);
            if (tempoDaIsr < 0) continue;
        }
        /* O pino lento chaveado na interrupcao (o LED piscando pelo timer): o nivel. */
        if (!tom.hz && agora - tom.ultimaBorda <= janela)
            ch(nullptr, "digitalWrite", cs_i(p), cs_i((int)((saida >> p) & 1)));
    }
}

static void escreverSaida(uint64_t nova)
{
    uint64_t mudou = nova ^ saida;
    saida = nova;
    for (int p = 0; mudou; p++, mudou >>= 1)
        if (mudou & 1) mudarPino(p, (int)((nova >> p) & 1));
}

/* O W1TS e o W1TC: os pinos da mascara vao para o nivel, qualquer que seja o
 * que o registrador guardava -- o pino pode ter mudado por fora dele (o
 * gpio_set_level, uma biblioteca). So a mudanca conta como borda para o tom. */
static void porPinos(uint64_t mascara, int nivel)
{
    uint64_t antes = saida;
    saida = nivel ? saida | mascara : saida & ~mascara;
    for (int p = 0; mascara; p++, mascara >>= 1) {
        if (!(mascara & 1)) continue;
        if ((int)((antes >> p) & 1) != nivel) mudarPino(p, nivel);
        else if (!tons[p].hz && tempoDaIsr < 0) ch(nullptr, "digitalWrite", cs_i(p), cs_i(nivel));
    }
}

/* O digitalWrite() do sketch: o registrador de saida guarda o nivel novo. */
extern "C" void cs_reg_pino(int p, int nivel)
{
    if (p < 0 || p >= 64) return;
    if (nivel) saida |= 1ULL << p;
    else saida &= ~(1ULL << p);
}

static void escreverHabilitado(uint64_t novo)
{
    uint64_t mudou = novo ^ habilitado;
    habilitado = novo;
    for (int p = 0; mudou; p++, mudou >>= 1)
        if (mudou & 1) ch(nullptr, "pinMode", cs_i(p), cs_i((novo >> p) & 1 ? OUTPUT : INPUT));
}

/* ---- o SPI pelos registradores ----
 * Quem fala direto com o SPI do chip (a TFT_eSPI) poe os bytes em W0..W15,
 * o tamanho em bits no MOSI_DLEN e dispara com o SPI_USR do CMD; o bit volta
 * a zero quando a transferencia acaba. Aqui o disparo manda os bytes pelo
 * barramento do core que e esse periferico (o SPI, ou o segundo -- o HSPI),
 * com os pinos e a velocidade que o SPI.begin() e o beginTransaction()
 * deram. Os outros registradores do SPI guardam o que foi escrito. */
struct SpiDoChip {
    uint32_t base;
    const char *nome;
    uint32_t r[64];
};
static SpiDoChip spisDoChip[] = {
#if CONFIG_IDF_TARGET_ESP32
    {DR_REG_SPI3_BASE, "SPI", {0}},  /* o VSPI: o SPI do Arduino */
    {DR_REG_SPI2_BASE, "SPI1", {0}}, /* o HSPI */
#else
    {DR_REG_SPI2_BASE, "SPI", {0}},  /* o FSPI */
#ifdef DR_REG_SPI3_BASE
    {DR_REG_SPI3_BASE, "SPI1", {0}},
#endif
#endif
};

/* O indice de um registrador do SPI (o deslocamento dele em palavras). */
#define CS_SPI_REG(reg) ((int)(((uint32_t)(reg(2)) - (uint32_t)DR_REG_SPI2_BASE) / 4))

static SpiDoChip *spiDoRegistrador(uint32_t r)
{
    for (auto &s : spisDoChip)
        if (r >= s.base && r < s.base + sizeof(s.r)) return &s;
    return nullptr;
}

static uint8_t aoContrario(uint8_t b)
{
    b = (uint8_t)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
    b = (uint8_t)((b & 0xCC) >> 2 | (b & 0x33) << 2);
    return (uint8_t)((b & 0xAA) >> 1 | (b & 0x55) << 1);
}

static void dispararSpi(SpiDoChip &s)
{
    uint32_t user = s.r[CS_SPI_REG(SPI_USER_REG)], ctrl = s.r[CS_SPI_REG(SPI_CTRL_REG)];
    bool sai = (user & SPI_USR_MOSI) != 0, entra = (user & SPI_USR_MISO) != 0;
#ifdef SPI_MOSI_DLEN_REG
    int bitsSai = (int)(s.r[CS_SPI_REG(SPI_MOSI_DLEN_REG)] & 0xFFFFFF) + 1;
    int bitsEntra = (int)(s.r[CS_SPI_REG(SPI_MISO_DLEN_REG)] & 0xFFFFFF) + 1;
#else
    int bitsSai = (int)(s.r[CS_SPI_REG(SPI_MS_DLEN_REG)] & 0x3FFFF) + 1;
    int bitsEntra = bitsSai;
#endif
    int bits = sai && entra ? (bitsSai > bitsEntra ? bitsSai : bitsEntra) : sai ? bitsSai : entra ? bitsEntra : 0;
    int n = (bits + 7) / 8;
    if (n <= 0) return;
    if (n > 64) n = 64;

    /* W0 primeiro, o byte baixo de cada palavra primeiro; com o HIGHPART, de W8 em diante. */
    const uint8_t *w = (const uint8_t *)&s.r[CS_SPI_REG(SPI_W0_REG)];
    uint8_t manda[64], chega[64];
    int inicio = sai && (user & SPI_USR_MOSI_HIGHPART) ? 32 : 0;
    for (int i = 0; i < n; i++) manda[i] = sai && inicio + i < 64 ? w[inicio + i] : 0xFF;
    bool baixoPrimeiro = ((ctrl >> SPI_WR_BIT_ORDER_S) & SPI_WR_BIT_ORDER_V) == 1;
    if (baixoPrimeiro)
        for (int i = 0; i < n; i++) manda[i] = aoContrario(manda[i]);

    if (!entra) {
        ch(s.nome, "writeBytes", cs_de(cs_vetor(manda, n)), cs_i(n));
        return;
    }
    memset(chega, 0, sizeof(chega));
    ch(s.nome, "transferBytes", cs_de(cs_vetor(manda, n)), cs_de(cs_vetor_mutavel(chega, n)), cs_i(n));
    if (((ctrl >> SPI_RD_BIT_ORDER_S) & SPI_RD_BIT_ORDER_V) == 1)
        for (int i = 0; i < n; i++) chega[i] = aoContrario(chega[i]);
    memcpy(&s.r[CS_SPI_REG(SPI_W0_REG)], chega, (size_t)n);
}

static void escreverNoSpi(uint32_t r, uint32_t v)
{
    SpiDoChip *s = spiDoRegistrador(r);
    if (!s) return;
    int i = (int)((r - s->base) / 4);
    if (i != CS_SPI_REG(SPI_CMD_REG)) {
        s->r[i] = v;
        return;
    }
    /* O disparo (e, nos chips novos, o SPI_UPDATE que leva a configuracao): os bits voltam a zero sozinhos. */
    uint32_t somem = SPI_USR;
#ifdef SPI_UPDATE
    somem |= SPI_UPDATE;
#endif
    s->r[i] = v & ~somem;
    if (v & SPI_USR) dispararSpi(*s);
}

/* O GPIO pelo endereco de verdade do chip e o mesmo registrador. */
static uint32_t enderecoDoGpio(uint32_t r)
{
    return (r & ~0xFFFu) == (uint32_t)CS_GPIO_DO_CHIP ? (uint32_t)DR_REG_GPIO_BASE | (r & 0xFFFu) : r;
}

extern "C" void cs_reg_escrever(uint32_t r, uint32_t v)
{
    r = enderecoDoGpio(r);
    switch (r) {
    case GPIO_OUT_REG: escreverSaida((saida & 0xFFFFFFFF00000000ULL) | v); break;
    case GPIO_OUT_W1TS_REG: porPinos(v, 1); break;
    case GPIO_OUT_W1TC_REG: porPinos(v, 0); break;
    case GPIO_OUT1_REG: escreverSaida((saida & 0xFFFFFFFFULL) | ((uint64_t)(v & 0xFF) << 32)); break;
    case GPIO_OUT1_W1TS_REG: porPinos((uint64_t)(v & 0xFF) << 32, 1); break;
    case GPIO_OUT1_W1TC_REG: porPinos((uint64_t)(v & 0xFF) << 32, 0); break;
    case GPIO_ENABLE_REG: escreverHabilitado((habilitado & 0xFFFFFFFF00000000ULL) | v); break;
    case GPIO_ENABLE_W1TS_REG: escreverHabilitado(habilitado | v); break;
    case GPIO_ENABLE_W1TC_REG: escreverHabilitado(habilitado & ~(uint64_t)v); break;
    case GPIO_ENABLE1_REG: escreverHabilitado((habilitado & 0xFFFFFFFFULL) | ((uint64_t)(v & 0xFF) << 32)); break;
    case GPIO_ENABLE1_W1TS_REG: escreverHabilitado(habilitado | ((uint64_t)(v & 0xFF) << 32)); break;
    case GPIO_ENABLE1_W1TC_REG: escreverHabilitado(habilitado & ~((uint64_t)(v & 0xFF) << 32)); break;
    default: escreverNoSpi(r, v); break;
    }
}

/* O que o registrador guarda, sem ir aos pinos: o que o ponteiro le antes
 * de uma escrita (o "|=" le e escreve), e os vizinhos de um acesso largo. */
extern "C" uint32_t cs_reg_guardado(uint32_t r)
{
    r = enderecoDoGpio(r);
    if (r == GPIO_IN_REG || r == GPIO_IN1_REG) return 0;
    return cs_reg_ler(r);
}

extern "C" uint32_t cs_reg_ler(uint32_t r)
{
    r = enderecoDoGpio(r);
    switch (r) {
    case GPIO_OUT_REG: case GPIO_OUT_W1TS_REG: case GPIO_OUT_W1TC_REG: return (uint32_t)saida;
    case GPIO_OUT1_REG: case GPIO_OUT1_W1TS_REG: case GPIO_OUT1_W1TC_REG: return (uint32_t)(saida >> 32) & 0xFF;
    case GPIO_ENABLE_REG: return (uint32_t)habilitado;
    case GPIO_ENABLE1_REG: return (uint32_t)(habilitado >> 32) & 0xFF;
    case GPIO_IN_REG:
    case GPIO_IN1_REG: {
        uint32_t v = 0;
        int base = r == GPIO_IN_REG ? 0 : 32, n = r == GPIO_IN_REG ? 32 : 8;
        for (int i = 0; i < n; i++)
            if (digitalRead(base + i)) v |= 1u << i;
        return v;
    }
    default:
        if (SpiDoChip *s = spiDoRegistrador(r)) return s->r[(r - s->base) / 4];
        return 0;
    }
}

/* ---- o timer ---- */
struct hw_timer_s {
    bool usado, contando, alarmeLigado, repete;
    uint32_t frequencia;
    uint64_t contagemBase;     /* a contagem em inicioUs */
    int64_t inicioUs;
    uint64_t alarme;
    int64_t proximoUs;
    void (*fn)(void);
    void (*fnArg)(void *);
    void *arg;
};
static hw_timer_s timers[4];

/* Para o vigia (SketchNativo): ha interrupcao de timer para atender. */
extern "C" __declspec(dllexport) volatile int cs_timers_ativos = 0;

static void recontar(void)
{
    int ativos = 0;
    for (auto &t : timers)
        if (t.usado && t.contando && t.alarmeLigado && (t.fn || t.fnArg)) ativos = 1;
    cs_timers_ativos = ativos;
}

static uint64_t contagem(hw_timer_t *t, int64_t agora)
{
    if (!t->contando) return t->contagemBase;
    return t->contagemBase + (uint64_t)((double)(agora - t->inicioUs) * t->frequencia / 1e6);
}

static void agendar(hw_timer_t *t)
{
    if (!t->alarmeLigado || !t->contando || !t->frequencia) return;
    int64_t agora = cs_relogio();
    uint64_t c = contagem(t, agora);
    uint64_t falta = t->alarme > c ? t->alarme - c : 0;
    t->proximoUs = agora + (int64_t)((double)falta * 1e6 / t->frequencia);
}

extern "C" hw_timer_t *cs_timer_comecar(uint32_t frequencia)
{
    for (auto &t : timers)
        if (!t.usado) {
            t = hw_timer_s();
            t.usado = t.contando = true;
            t.frequencia = frequencia ? frequencia : 1000000;
            t.inicioUs = cs_relogio();
            return &t;
        }
    return nullptr;
}

extern "C" hw_timer_t *cs_timer_comecar_v2(uint8_t num, uint16_t divider, bool countUp)
{
    (void)num;
    (void)countUp;
    return cs_timer_comecar(80000000u / (divider ? divider : 1));
}

extern "C" void timerEnd(hw_timer_t *t)
{
    if (!t) return;
    t->usado = false;
    recontar();
}

extern "C" void timerStart(hw_timer_t *t)
{
    if (!t || t->contando) return;
    t->contando = true;
    t->inicioUs = cs_relogio();
    agendar(t);
    recontar();
}

extern "C" void timerStop(hw_timer_t *t)
{
    if (!t || !t->contando) return;
    t->contagemBase = contagem(t, cs_relogio());
    t->contando = false;
    recontar();
}

extern "C" void timerRestart(hw_timer_t *t) { timerWrite(t, 0); }

extern "C" void timerWrite(hw_timer_t *t, uint64_t v)
{
    if (!t) return;
    t->contagemBase = v;
    t->inicioUs = cs_relogio();
    agendar(t);
}

extern "C" uint64_t timerRead(hw_timer_t *t) { return t ? contagem(t, cs_relogio()) : 0; }
extern "C" uint64_t timerReadMicros(hw_timer_t *t) { return t ? timerRead(t) * 1000000ULL / t->frequencia : 0; }
extern "C" uint64_t timerReadMilis(hw_timer_t *t) { return t ? timerRead(t) * 1000ULL / t->frequencia : 0; }
extern "C" double timerReadSeconds(hw_timer_t *t) { return t ? (double)timerRead(t) / t->frequencia : 0; }
extern "C" uint32_t timerGetFrequency(hw_timer_t *t) { return t ? t->frequencia : 0; }

extern "C" void cs_timer_rotina(hw_timer_t *t, void (*fn)(void))
{
    if (!t) return;
    t->fn = fn;
    t->fnArg = nullptr;
    recontar();
}

extern "C" void timerAttachInterruptArg(hw_timer_t *t, void (*fn)(void *), void *arg)
{
    if (!t) return;
    t->fnArg = fn;
    t->arg = arg;
    t->fn = nullptr;
    recontar();
}

extern "C" void timerDetachInterrupt(hw_timer_t *t)
{
    if (!t) return;
    t->fn = nullptr;
    t->fnArg = nullptr;
    recontar();
}

extern "C" void timerAlarm(hw_timer_t *t, uint64_t valor, bool repete, uint64_t recarga)
{
    if (!t) return;
    (void)recarga;
    t->alarme = valor ? valor : 1;
    t->repete = repete;
    t->alarmeLigado = true;
    agendar(t);
    recontar();
}

extern "C" void timerAlarmWrite(hw_timer_t *t, uint64_t valor, bool repete)
{
    if (!t) return;
    t->alarme = valor ? valor : 1;
    t->repete = repete;
    agendar(t);
}

extern "C" void timerAlarmEnable(hw_timer_t *t)
{
    if (!t) return;
    t->alarmeLigado = true;
    agendar(t);
    recontar();
}

extern "C" void timerAlarmDisable(hw_timer_t *t)
{
    if (!t) return;
    t->alarmeLigado = false;
    recontar();
}

extern "C" bool timerAlarmEnabled(hw_timer_t *t) { return t && t->alarmeLigado; }
extern "C" uint64_t timerAlarmRead(hw_timer_t *t) { return t ? t->alarme : 0; }
extern "C" void timerSetDivider(hw_timer_t *t, uint16_t d)
{
    if (t) t->frequencia = 80000000u / (d ? d : 1);
}
extern "C" uint16_t timerGetDivider(hw_timer_t *t) { return t ? (uint16_t)(80000000u / t->frequencia) : 0; }
extern "C" bool timerStarted(hw_timer_t *t) { return t && t->contando; }

/* As interrupcoes que venceram, em lote, cada uma no seu tempo (chamada pelo
 * simulador por cs_entrar(4), na thread do vigia ou no delay). */
static volatile int noLote;

extern "C" void cs_atender_timers(void)
{
    if (noLote) return;
    noLote = 1;
    int64_t agora = cs_relogio();
    if (agora >= 0) {
        for (auto &t : timers) {
            if (!t.usado || !t.contando || !t.alarmeLigado || !(t.fn || t.fnArg)) continue;
            double periodo = (double)t.alarme * 1e6 / t.frequencia;
            if (periodo < 1) periodo = 1;
            /* Muito atras (a placa parou no depurador): recomeca de agora. */
            if (agora - t.proximoUs > 100000) t.proximoUs = agora - 100000;
            int voltas = 0;
            double proximo = (double)t.proximoUs;
            while (proximo <= agora && voltas++ < 20000) {
                tempoDaIsr = (int64_t)proximo;
                if (t.fn) t.fn();
                else t.fnArg(t.arg);
                if (!t.repete) {
                    t.alarmeLigado = false;
                    break;
                }
                proximo += periodo;
            }
            t.proximoUs = (int64_t)proximo;
            tempoDaIsr = -1;
        }
        recontar();
        avaliarTons(agora);
    }
    noLote = 0;
}

#else
extern "C" void cs_atender_timers(void) {}
extern "C" __declspec(dllexport) volatile int cs_timers_ativos = 0;
#endif

#if defined(ESP32)
/* soc/dport_access.h: o DPORT_REG_READ da ESP32 (a leitura segura contra o outro nucleo). */
extern "C" uint32_t esp_dport_access_reg_read(uint32_t r) { return cs_reg_ler(r); }
#endif
