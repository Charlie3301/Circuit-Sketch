/* cs_idf_resto.cpp -- o resto do ESP-IDF que um programa comum usa: a flash crua
 * (esp_flash.h, spi_flash_mmap.h) sobre as particoes de cs_particoes.cpp, os eFuses
 * de uma placa virgem, o ADC continuo (adc_continuous.h) sobre o analogRead e o REPL
 * do esp_console sobre o monitor serial. */
#include "Arduino.h"
#if defined(ESP32)
#include "esp_flash.h"
#include "esp_partition.h"
#include "spi_flash_mmap.h"
#include "esp_efuse.h"
#include "esp_console.h"
#include "linenoise/linenoise.h"
#include "freertos/task.h"
#include "freertos/semphr.h"
#if SOC_ADC_DMA_SUPPORTED
#include "esp_adc/adc_continuous.h"
#include "esp_adc/adc_oneshot.h"
#endif
#include <deque>
#include <map>
#include <string>
#include <vector>

/* =====================================================================
 * a flash crua: o que cai fora da tabela de particoes (o bootloader, a propria
 * tabela) le como apagado e nao guarda nada */
namespace {
const uint32_t TAMANHO_DA_FLASH = 4u * 1024 * 1024;
const uint32_t SETOR = 4096;

const esp_partition_t *particaoDe(uint32_t endereco, uint32_t &ate)
{
    const esp_partition_t *achada = nullptr;
    ate = TAMANHO_DA_FLASH;
    esp_partition_iterator_t it = esp_partition_find(ESP_PARTITION_TYPE_ANY, ESP_PARTITION_SUBTYPE_ANY, nullptr);
    while (it) {
        const esp_partition_t *p = esp_partition_get(it);
        if (endereco >= p->address && endereco < p->address + p->size) {
            achada = p;
            ate = p->address + p->size;
        } else if (p->address > endereco && p->address < ate && !achada) {
            ate = p->address;
        }
        it = esp_partition_next(it);
    }
    return achada;
}
esp_flash_t chipPadrao = {};
std::map<spi_flash_mmap_handle_t, void *> *mapasDaFlash;
} /* namespace */

extern "C" {

esp_flash_t *esp_flash_default_chip = &chipPadrao;

esp_err_t esp_flash_read(esp_flash_t *, void *destino, uint32_t endereco, uint32_t tamanho)
{
    if (!destino) return ESP_ERR_INVALID_ARG;
    if ((uint64_t)endereco + tamanho > TAMANHO_DA_FLASH) return ESP_ERR_INVALID_ARG;
    uint8_t *d = (uint8_t *)destino;
    while (tamanho) {
        uint32_t ate;
        const esp_partition_t *p = particaoDe(endereco, ate);
        uint32_t n = ate - endereco < tamanho ? ate - endereco : tamanho;
        if (p) {
            esp_err_t e = esp_partition_read(p, endereco - p->address, d, n);
            if (e != ESP_OK) return e;
        } else {
            memset(d, 0xFF, n);
        }
        d += n;
        endereco += n;
        tamanho -= n;
    }
    return ESP_OK;
}
esp_err_t esp_flash_write(esp_flash_t *, const void *origem, uint32_t endereco, uint32_t tamanho)
{
    if (!origem) return ESP_ERR_INVALID_ARG;
    if ((uint64_t)endereco + tamanho > TAMANHO_DA_FLASH) return ESP_ERR_INVALID_ARG;
    const uint8_t *o = (const uint8_t *)origem;
    while (tamanho) {
        uint32_t ate;
        const esp_partition_t *p = particaoDe(endereco, ate);
        uint32_t n = ate - endereco < tamanho ? ate - endereco : tamanho;
        if (p) {
            esp_err_t e = esp_partition_write(p, endereco - p->address, o, n);
            if (e != ESP_OK) return e;
        }
        o += n;
        endereco += n;
        tamanho -= n;
    }
    return ESP_OK;
}
esp_err_t esp_flash_erase_region(esp_flash_t *, uint32_t inicio, uint32_t tamanho)
{
    if (inicio % SETOR || tamanho % SETOR) return ESP_ERR_INVALID_ARG;
    if ((uint64_t)inicio + tamanho > TAMANHO_DA_FLASH) return ESP_ERR_INVALID_ARG;
    while (tamanho) {
        uint32_t ate;
        const esp_partition_t *p = particaoDe(inicio, ate);
        uint32_t n = ate - inicio < tamanho ? ate - inicio : tamanho;
        if (p) {
            esp_err_t e = esp_partition_erase_range(p, inicio - p->address, n);
            if (e != ESP_OK) return e;
        }
        inicio += n;
        tamanho -= n;
    }
    return ESP_OK;
}
esp_err_t esp_flash_erase_chip(esp_flash_t *chip) { return esp_flash_erase_region(chip, 0, TAMANHO_DA_FLASH); }
esp_err_t esp_flash_read_id(esp_flash_t *, uint32_t *id)
{
    if (!id) return ESP_ERR_INVALID_ARG;
    *id = 0x204016; /* uma XMC de 4 MB, a mais comum nos modulos */
    return ESP_OK;
}
esp_err_t esp_flash_read_unique_chip_id(esp_flash_t *, uint64_t *id)
{
    if (!id) return ESP_ERR_INVALID_ARG;
    *id = ESP.getEfuseMac();
    return ESP_OK;
}
esp_err_t esp_flash_get_physical_size(esp_flash_t *, uint32_t *tamanho)
{
    if (!tamanho) return ESP_ERR_INVALID_ARG;
    *tamanho = TAMANHO_DA_FLASH;
    return ESP_OK;
}
esp_err_t esp_flash_get_chip_write_protect(esp_flash_t *, bool *protegida)
{
    if (!protegida) return ESP_ERR_INVALID_ARG;
    *protegida = false;
    return ESP_OK;
}
esp_err_t esp_flash_init(esp_flash_t *) { return ESP_OK; }
bool esp_flash_chip_driver_initialized(const esp_flash_t *) { return true; }
uint32_t esp_partition_get_main_flash_sector_size(void) { return SETOR; }

/* O mapeamento e uma copia de quando se pediu: na placa ele e a propria flash, mas
 * ninguem escreve enquanto esta mapeado (o IDF proibe). */
esp_err_t spi_flash_mmap(size_t origem, size_t tamanho, spi_flash_mmap_memory_t, const void **saida, spi_flash_mmap_handle_t *alca)
{
    if (!saida || !alca || !tamanho) return ESP_ERR_INVALID_ARG;
    if (origem % CONFIG_MMU_PAGE_SIZE) return ESP_ERR_INVALID_ARG;
    void *b = malloc(tamanho);
    if (!b) return ESP_ERR_NO_MEM;
    esp_err_t e = esp_flash_read(nullptr, b, (uint32_t)origem, (uint32_t)tamanho);
    if (e != ESP_OK) {
        free(b);
        return e;
    }
    static spi_flash_mmap_handle_t proxima = 1;
    if (!mapasDaFlash) mapasDaFlash = new std::map<spi_flash_mmap_handle_t, void *>();
    (*mapasDaFlash)[proxima] = b;
    *saida = b;
    *alca = proxima++;
    return ESP_OK;
}
void spi_flash_munmap(spi_flash_mmap_handle_t alca)
{
    if (!mapasDaFlash) return;
    auto it = mapasDaFlash->find(alca);
    if (it == mapasDaFlash->end()) return;
    free(it->second);
    mapasDaFlash->erase(it);
}
uint32_t spi_flash_mmap_get_free_pages(spi_flash_mmap_memory_t) { return 64; }

/* =====================================================================
 * os eFuses: os de uma placa virgem, tudo em zero */
esp_err_t esp_efuse_read_field_blob(const esp_efuse_desc_t *campo[], void *destino, size_t bits)
{
    if (!campo || !destino || !bits) return ESP_ERR_INVALID_ARG;
    memset(destino, 0, (bits + 7) / 8);
    return ESP_OK;
}
bool esp_efuse_read_field_bit(const esp_efuse_desc_t *[]) { return false; }
esp_err_t esp_efuse_read_field_cnt(const esp_efuse_desc_t *campo[], size_t *quantos)
{
    if (!campo || !quantos) return ESP_ERR_INVALID_ARG;
    *quantos = 0;
    return ESP_OK;
}
int esp_efuse_get_field_size(const esp_efuse_desc_t *campo[])
{
    int bits = 0;
    for (int i = 0; campo && campo[i]; i++) bits += campo[i]->bit_count;
    return bits;
}
uint32_t esp_efuse_read_reg(esp_efuse_block_t, unsigned int) { return 0; }
esp_err_t esp_efuse_read_block(esp_efuse_block_t, void *destino, size_t, size_t bits)
{
    if (!destino) return ESP_ERR_INVALID_ARG;
    memset(destino, 0, (bits + 7) / 8);
    return ESP_OK;
}
esp_efuse_coding_scheme_t esp_efuse_get_coding_scheme(esp_efuse_block_t) { return EFUSE_CODING_SCHEME_NONE; }
uint32_t esp_efuse_get_pkg_ver(void) { return 0; }
uint32_t esp_efuse_read_secure_version(void) { return 0; }
esp_err_t esp_efuse_mac_get_custom(uint8_t *) { return ESP_ERR_INVALID_MAC; }
} /* extern "C" */

/* =====================================================================
 * o ADC continuo: uma tarefa le os canais com o analogRead e entrega um quadro por
 * vez, no ritmo que a configuracao pediu */
#if SOC_ADC_DMA_SUPPORTED
struct adc_continuous_ctx_t {
    uint32_t guardaMaxima = 0, tamanhoDoQuadro = 0, frequencia = 20000;
    bool esvaziaCheia = false, ligado = false;
    std::vector<adc_digi_pattern_config_t> padrao;
    adc_digi_output_format_t formato = ADC_DIGI_OUTPUT_FORMAT_TYPE1;
    adc_continuous_evt_cbs_t cbs = {};
    void *contexto = nullptr;
    std::deque<uint8_t> guarda;
    SemaphoreHandle_t trava = nullptr;
    TaskHandle_t tarefa = nullptr;
};

static int pinoDoCanal(int unidade, int canal)
{
    for (int io = 0; io < 49; io++) {
        adc_unit_t u;
        adc_channel_t c;
        if (adc_oneshot_io_to_channel(io, &u, &c) == ESP_OK && (int)u == unidade && (int)c == canal) return io;
    }
    return -1;
}

static void amostra(adc_continuous_ctx_t *h, const adc_digi_pattern_config_t &p, uint8_t *saida)
{
    int io = pinoDoCanal(p.unit, p.channel);
    uint32_t leitura = io >= 0 ? (uint32_t)analogRead((uint8_t)io) : 0;
    adc_digi_output_data_t d = {};
#if CONFIG_IDF_TARGET_ESP32
    (void)h;
    d.type1.data = leitura & 0xFFF;
    d.type1.channel = p.channel;
#elif CONFIG_IDF_TARGET_ESP32S2
    if (h->formato == ADC_DIGI_OUTPUT_FORMAT_TYPE1) {
        d.type1.data = leitura & 0xFFF;
        d.type1.channel = p.channel;
    } else {
        d.type2.data = (leitura >> 1) & 0x7FF;
        d.type2.channel = p.channel;
        d.type2.unit = p.unit;
    }
#else
    (void)h;
    d.type2.data = leitura & 0xFFF;
    d.type2.channel = p.channel;
#if !CONFIG_IDF_TARGET_ESP32C6 && !CONFIG_IDF_TARGET_ESP32H2
    d.type2.unit = p.unit;
#endif
#endif
    memcpy(saida, &d, SOC_ADC_DIGI_RESULT_BYTES);
}

static void tarefaDoAdc(void *arg)
{
    auto *h = (adc_continuous_ctx_t *)arg;
    std::vector<uint8_t> quadro(h->tamanhoDoQuadro);
    uint32_t porQuadro = h->tamanhoDoQuadro / SOC_ADC_DIGI_RESULT_BYTES;
    uint32_t espera = porQuadro * 1000 / (h->frequencia ? h->frequencia : 1);
    if (espera < 1) espera = 1;
    size_t proximo = 0;
    while (h->ligado) {
        for (uint32_t i = 0; i < porQuadro; i++) {
            amostra(h, h->padrao[proximo], quadro.data() + i * SOC_ADC_DIGI_RESULT_BYTES);
            proximo = (proximo + 1) % h->padrao.size();
        }
        adc_continuous_evt_data_t ev = {quadro.data(), h->tamanhoDoQuadro};
        bool cheia = false;
        xSemaphoreTake(h->trava, portMAX_DELAY);
        if (h->guarda.size() + quadro.size() > h->guardaMaxima) {
            cheia = true;
            if (h->esvaziaCheia) h->guarda.clear();
        }
        if (h->guarda.size() + quadro.size() <= h->guardaMaxima) h->guarda.insert(h->guarda.end(), quadro.begin(), quadro.end());
        xSemaphoreGive(h->trava);
        if (h->cbs.on_conv_done) h->cbs.on_conv_done(h, &ev, h->contexto);
        if (cheia && h->cbs.on_pool_ovf) h->cbs.on_pool_ovf(h, &ev, h->contexto);
        vTaskDelay(pdMS_TO_TICKS(espera));
    }
    h->tarefa = nullptr;
    vTaskDelete(nullptr);
}

extern "C" {
esp_err_t adc_continuous_new_handle(const adc_continuous_handle_cfg_t *cfg, adc_continuous_handle_t *saida)
{
    if (!cfg || !saida || !cfg->conv_frame_size || cfg->conv_frame_size % SOC_ADC_DIGI_DATA_BYTES_PER_CONV) return ESP_ERR_INVALID_ARG;
    auto *h = new adc_continuous_ctx_t();
    h->guardaMaxima = cfg->max_store_buf_size;
    h->tamanhoDoQuadro = cfg->conv_frame_size;
    h->esvaziaCheia = cfg->flags.flush_pool;
    h->trava = xSemaphoreCreateMutex();
    *saida = h;
    return ESP_OK;
}
esp_err_t adc_continuous_config(adc_continuous_handle_t h, const adc_continuous_config_t *cfg)
{
    if (!h || !cfg || !cfg->pattern_num || !cfg->adc_pattern) return ESP_ERR_INVALID_ARG;
    if (h->ligado) return ESP_ERR_INVALID_STATE;
    if (cfg->sample_freq_hz < SOC_ADC_SAMPLE_FREQ_THRES_LOW || cfg->sample_freq_hz > SOC_ADC_SAMPLE_FREQ_THRES_HIGH) return ESP_ERR_INVALID_ARG;
    h->padrao.assign(cfg->adc_pattern, cfg->adc_pattern + cfg->pattern_num);
    h->frequencia = cfg->sample_freq_hz;
    h->formato = cfg->format;
    return ESP_OK;
}
esp_err_t adc_continuous_register_event_callbacks(adc_continuous_handle_t h, const adc_continuous_evt_cbs_t *cbs, void *contexto)
{
    if (!h || !cbs) return ESP_ERR_INVALID_ARG;
    if (h->ligado) return ESP_ERR_INVALID_STATE;
    h->cbs = *cbs;
    h->contexto = contexto;
    return ESP_OK;
}
esp_err_t adc_continuous_start(adc_continuous_handle_t h)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    if (h->ligado || h->padrao.empty()) return ESP_ERR_INVALID_STATE;
    h->ligado = true;
    xTaskCreate(tarefaDoAdc, "adc_cont", 4096, h, 5, &h->tarefa);
    return ESP_OK;
}
esp_err_t adc_continuous_stop(adc_continuous_handle_t h)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    if (!h->ligado) return ESP_ERR_INVALID_STATE;
    h->ligado = false;
    while (h->tarefa) vTaskDelay(1);
    return ESP_OK;
}
esp_err_t adc_continuous_read(adc_continuous_handle_t h, uint8_t *destino, uint32_t maximo, uint32_t *lidos, uint32_t ms)
{
    if (!h || !destino || !lidos) return ESP_ERR_INVALID_ARG;
    if (!h->ligado) return ESP_ERR_INVALID_STATE;
    TickType_t inicio = xTaskGetTickCount();
    uint32_t n = 0;
    for (;;) {
        xSemaphoreTake(h->trava, portMAX_DELAY);
        while (n < maximo && !h->guarda.empty()) {
            destino[n++] = h->guarda.front();
            h->guarda.pop_front();
        }
        xSemaphoreGive(h->trava);
        if (n >= maximo || (n && ms == 0)) break;
        if (ms != ADC_MAX_DELAY && (xTaskGetTickCount() - inicio) >= pdMS_TO_TICKS(ms)) break;
        vTaskDelay(1);
    }
    *lidos = n;
    return n ? ESP_OK : ESP_ERR_TIMEOUT;
}
esp_err_t adc_continuous_flush_pool(adc_continuous_handle_t h)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    xSemaphoreTake(h->trava, portMAX_DELAY);
    h->guarda.clear();
    xSemaphoreGive(h->trava);
    return ESP_OK;
}
esp_err_t adc_continuous_deinit(adc_continuous_handle_t h)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    if (h->ligado) return ESP_ERR_INVALID_STATE;
    vSemaphoreDelete(h->trava);
    delete h;
    return ESP_OK;
}
esp_err_t adc_continuous_io_to_channel(int io, adc_unit_t *const unidade, adc_channel_t *const canal)
{
    return adc_oneshot_io_to_channel(io, unidade, canal);
}
esp_err_t adc_continuous_channel_to_io(adc_unit_t unidade, adc_channel_t canal, int *const io)
{
    if (!io) return ESP_ERR_INVALID_ARG;
    *io = pinoDoCanal(unidade, canal);
    return *io < 0 ? ESP_ERR_INVALID_ARG : ESP_OK;
}
} /* extern "C" */
#endif /* SOC_ADC_DMA_SUPPORTED */

/* =====================================================================
 * o REPL do esp_console: uma tarefa le as linhas do monitor serial pelo linenoise
 * e roda os comandos, com os mesmos textos do esp_console_repl_chip.c */
namespace {
struct ReplDoSimulador {
    esp_console_repl_t base;
    std::string prompt;
    uint32_t pilha, prioridade;
    TaskHandle_t tarefa;
    volatile bool parar;
};

void tarefaDoRepl(void *arg)
{
    auto *r = (ReplDoSimulador *)arg;
    printf("\r\nType 'help' to get the list of commands.\r\n"
           "Use UP/DOWN arrows to navigate through command history.\r\n"
           "Press TAB when typing command name to auto-complete.\r\n");
    while (!r->parar) {
        char *linha = linenoise(r->prompt.c_str());
        if (!linha) continue;
        if (*linha) linenoiseHistoryAdd(linha);
        int volta = 0;
        esp_err_t e = esp_console_run(linha, &volta);
        if (e == ESP_ERR_NOT_FOUND) printf("Unrecognized command\n");
        else if (e == ESP_ERR_INVALID_ARG) { /* a linha estava vazia */ }
        else if (e == ESP_OK && volta != ESP_OK) printf("Command returned non-zero error code: 0x%x (%s)\n", volta, esp_err_to_name(volta));
        else if (e != ESP_OK) printf("Internal error: %s\n", esp_err_to_name(e));
        linenoiseFree(linha);
    }
    r->tarefa = nullptr;
    vTaskDelete(nullptr);
}

esp_err_t apagarRepl(esp_console_repl_t *base)
{
    auto *r = (ReplDoSimulador *)base;
    r->parar = true;
    esp_console_deinit();
    return ESP_OK;
}

esp_err_t novoRepl(const esp_console_repl_config_t *cfg, esp_console_repl_t **saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    esp_console_config_t c = ESP_CONSOLE_CONFIG_DEFAULT();
    if (cfg->max_cmdline_length) c.max_cmdline_length = cfg->max_cmdline_length;
    esp_err_t e = esp_console_init(&c);
    if (e != ESP_OK) return e;
    linenoiseSetCompletionCallback(&esp_console_get_completion);
    linenoiseSetHintsCallback((linenoiseHintsCallback *)&esp_console_get_hint);
    linenoiseHistorySetMaxLen((int)cfg->max_history_len);
    if (cfg->history_save_path) linenoiseHistoryLoad(cfg->history_save_path);
    auto *r = new ReplDoSimulador();
    r->base.del = apagarRepl;
    r->prompt = cfg->prompt ? cfg->prompt : "esp> ";
    r->pilha = cfg->task_stack_size ? cfg->task_stack_size : 4096;
    r->prioridade = cfg->task_priority ? cfg->task_priority : 2;
    r->tarefa = nullptr;
    r->parar = false;
    *saida = &r->base;
    return ESP_OK;
}
} /* namespace */

extern "C" {
#if CONFIG_ESP_CONSOLE_UART_DEFAULT || CONFIG_ESP_CONSOLE_UART_CUSTOM
esp_err_t esp_console_new_repl_uart(const esp_console_dev_uart_config_t *, const esp_console_repl_config_t *cfg, esp_console_repl_t **saida)
{
    return novoRepl(cfg, saida);
}
#endif
#if CONFIG_ESP_CONSOLE_USB_CDC
esp_err_t esp_console_new_repl_usb_cdc(const esp_console_dev_usb_cdc_config_t *, const esp_console_repl_config_t *cfg, esp_console_repl_t **saida)
{
    return novoRepl(cfg, saida);
}
#endif
#if CONFIG_ESP_CONSOLE_USB_SERIAL_JTAG
esp_err_t esp_console_new_repl_usb_serial_jtag(const esp_console_dev_usb_serial_jtag_config_t *, const esp_console_repl_config_t *cfg, esp_console_repl_t **saida)
{
    return novoRepl(cfg, saida);
}
#endif
esp_err_t esp_console_start_repl(esp_console_repl_t *base)
{
    if (!base) return ESP_ERR_INVALID_ARG;
    auto *r = (ReplDoSimulador *)base;
    if (r->tarefa) return ESP_ERR_INVALID_STATE;
    xTaskCreate(tarefaDoRepl, "console_repl", r->pilha, r, r->prioridade, &r->tarefa);
    return ESP_OK;
}
esp_err_t esp_console_stop_repl(esp_console_repl_t *base)
{
    if (!base) return ESP_ERR_INVALID_ARG;
    return base->del(base);
}
} /* extern "C" */
#endif
