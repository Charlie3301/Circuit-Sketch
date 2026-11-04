/* cs_idf_nvs.cpp -- o NVS do ESP-IDF (nvs.h) no simulador: as chaves ficam na NVS
 * da placa (Maquina.Nvs), que sobrevive ao reinicio como a flash. O Preferences
 * do core e o de verdade, por cima daqui, entao um le o que o outro grava. O
 * tipo fica com a chave: ler com outro tipo responde ESP_ERR_NVS_NOT_FOUND. */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_objeto.h"
#include "nvs.h"
#include "nvs_flash.h"
#include <map>
#include <string>
#include <vector>

/* O programa do IDF tem que chamar o nvs_flash_init; no Arduino o core ja chamou. */
extern "C" {
int cs_nvs_iniciada = 1;
}

namespace {
/* O namespace existe depois de aberto para escrita, mesmo sem chaves. */
const char *const MARCA_DO_ESPACO = "\x01";
const int TIPO_DA_MARCA = 0xFE;
const size_t CHAVES_NA_FLASH = 504;

struct Aberto {
    std::string espaco;
    bool soLeitura;
};
std::map<nvs_handle_t, Aberto> abertos;
nvs_handle_t proximo = 1;

Aberto *achar(nvs_handle_t h)
{
    auto i = abertos.find(h);
    return i == abertos.end() ? nullptr : &i->second;
}
bool chaveValida(const char *k) { return k && k[0] && strlen(k) <= NVS_KEY_NAME_MAX_SIZE - 1; }
int tipoNaFlash(const std::string &espaco, const char *chave) { return (int)cs_obj_i(nullptr, "__nvs_tipo", espaco.c_str(), chave); }
int tamanhoNaFlash(const std::string &espaco, const char *chave) { return (int)cs_obj_i(nullptr, "__nvs_tamanho", espaco.c_str(), chave); }
void gravarNaFlash(const std::string &espaco, const char *chave, int tipo, const void *dados, size_t n)
{
    static const uint8_t vazio = 0;
    cs_obj_n(nullptr, "__nvs_gravar", espaco.c_str(), chave, tipo, cs_vetor(n ? dados : &vazio, (int)n));
}
void lerDaFlash(const std::string &espaco, const char *chave, void *dados, size_t n) { cs_obj_i(nullptr, "__nvs_ler", espaco.c_str(), chave, cs_vetor_mutavel(dados, (int)n)); }

std::vector<std::string> chavesDe(const std::string &espaco)
{
    std::vector<std::string> saida;
    String lista = cs_obj_t(nullptr, "__nvs_chaves", espaco.c_str());
    int i = 0;
    while (i < (int)lista.length()) {
        int fim = lista.indexOf('\n', i);
        if (fim < 0) fim = lista.length();
        String k = lista.substring(i, fim);
        if (k.length() && k[0] != MARCA_DO_ESPACO[0]) saida.push_back(k.c_str());
        i = fim + 1;
    }
    return saida;
}

esp_err_t abrir(const char *espaco, nvs_open_mode_t modo, nvs_handle_t *saida)
{
    if (!cs_nvs_iniciada) return ESP_ERR_NVS_NOT_INITIALIZED;
    if (!saida) return ESP_ERR_INVALID_ARG;
    if (!espaco || !espaco[0] || strlen(espaco) > NVS_NS_NAME_MAX_SIZE - 1) return ESP_ERR_NVS_INVALID_NAME;
    bool existe = tipoNaFlash(espaco, MARCA_DO_ESPACO) >= 0 || !chavesDe(espaco).empty();
    if (!existe) {
        if (modo == NVS_READONLY) return ESP_ERR_NVS_NOT_FOUND;
        gravarNaFlash(espaco, MARCA_DO_ESPACO, TIPO_DA_MARCA, nullptr, 0);
    }
    *saida = proximo++;
    abertos[*saida] = {espaco, modo == NVS_READONLY};
    return ESP_OK;
}

esp_err_t podeEscrever(Aberto *a, const char *chave)
{
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (a->soLeitura) return ESP_ERR_NVS_READ_ONLY;
    if (!chaveValida(chave)) return ESP_ERR_NVS_INVALID_NAME;
    return ESP_OK;
}

esp_err_t gravar(nvs_handle_t h, const char *chave, nvs_type_t tipo, const void *dados, size_t n)
{
    Aberto *a = achar(h);
    esp_err_t e = podeEscrever(a, chave);
    if (e != ESP_OK) return e;
    int antes = tipoNaFlash(a->espaco, chave);
    if (antes >= 0 && antes != (int)tipo && (antes == NVS_TYPE_BLOB || antes == NVS_TYPE_STR || tipo == NVS_TYPE_BLOB || tipo == NVS_TYPE_STR)) return ESP_ERR_NVS_TYPE_MISMATCH;
    gravarNaFlash(a->espaco, chave, (int)tipo, dados, n);
    return ESP_OK;
}

esp_err_t lerNumero(nvs_handle_t h, const char *chave, nvs_type_t tipo, void *saida, size_t n)
{
    Aberto *a = achar(h);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (!chaveValida(chave)) return ESP_ERR_NVS_INVALID_NAME;
    if (!saida) return ESP_ERR_INVALID_ARG;
    if (tipoNaFlash(a->espaco, chave) != (int)tipo) return ESP_ERR_NVS_NOT_FOUND;
    lerDaFlash(a->espaco, chave, saida, n);
    return ESP_OK;
}

esp_err_t lerVariavel(nvs_handle_t h, const char *chave, nvs_type_t tipo, void *saida, size_t *tamanho)
{
    Aberto *a = achar(h);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (!chaveValida(chave)) return ESP_ERR_NVS_INVALID_NAME;
    if (!tamanho) return ESP_ERR_INVALID_ARG;
    if (tipoNaFlash(a->espaco, chave) != (int)tipo) return ESP_ERR_NVS_NOT_FOUND;
    size_t precisa = (size_t)tamanhoNaFlash(a->espaco, chave);
    if (saida) {
        if (*tamanho < precisa) {
            *tamanho = precisa;
            return ESP_ERR_NVS_INVALID_LENGTH;
        }
        if (precisa) lerDaFlash(a->espaco, chave, saida, precisa);
    }
    *tamanho = precisa;
    return ESP_OK;
}
} /* namespace */

struct nvs_opaque_iterator_t {
    std::vector<nvs_entry_info_t> entradas;
    size_t pos;
};

static esp_err_t iterar(const std::string &espaco, nvs_type_t tipo, nvs_iterator_t *saida)
{
    auto *it = new nvs_opaque_iterator_t();
    it->pos = 0;
    for (const std::string &k : chavesDe(espaco)) {
        int t = tipoNaFlash(espaco, k.c_str());
        if (t < 0 || (tipo != NVS_TYPE_ANY && t != (int)tipo)) continue;
        nvs_entry_info_t e = {};
        snprintf(e.namespace_name, sizeof(e.namespace_name), "%s", espaco.c_str());
        snprintf(e.key, sizeof(e.key), "%s", k.c_str());
        e.type = (nvs_type_t)t;
        it->entradas.push_back(e);
    }
    if (it->entradas.empty()) {
        delete it;
        *saida = nullptr;
        return ESP_ERR_NVS_NOT_FOUND;
    }
    *saida = it;
    return ESP_OK;
}

#define CS_NUMERO(sufixo, tipo_c, tipo_nvs)                                                                                                          \
    esp_err_t nvs_set_##sufixo(nvs_handle_t h, const char *k, tipo_c v) { return gravar(h, k, tipo_nvs, &v, sizeof(v)); }                            \
    esp_err_t nvs_get_##sufixo(nvs_handle_t h, const char *k, tipo_c *v) { return lerNumero(h, k, tipo_nvs, v, sizeof(*v)); }

extern "C" {

esp_err_t nvs_flash_init(void)
{
    cs_nvs_iniciada = 1;
    return ESP_OK;
}
esp_err_t nvs_flash_deinit(void)
{
    if (!cs_nvs_iniciada) return ESP_ERR_NVS_NOT_INITIALIZED;
    cs_nvs_iniciada = 0;
    return ESP_OK;
}
esp_err_t nvs_flash_erase(void)
{
    cs_obj_n(nullptr, "__nvs_limpar");
    return ESP_OK;
}
esp_err_t nvs_flash_init_partition(const char *partition_label)
{
    (void)partition_label;
    return nvs_flash_init();
}
esp_err_t nvs_flash_deinit_partition(const char *partition_label)
{
    (void)partition_label;
    return nvs_flash_deinit();
}
esp_err_t nvs_flash_erase_partition(const char *part_name)
{
    (void)part_name;
    return nvs_flash_erase();
}
esp_err_t nvs_flash_init_partition_ptr(const esp_partition_t *partition)
{
    (void)partition;
    return nvs_flash_init();
}
esp_err_t nvs_flash_erase_partition_ptr(const esp_partition_t *partition)
{
    (void)partition;
    return nvs_flash_erase();
}

esp_err_t nvs_open(const char *namespace_name, nvs_open_mode_t open_mode, nvs_handle_t *out_handle) { return abrir(namespace_name, open_mode, out_handle); }
esp_err_t nvs_open_from_partition(const char *part_name, const char *namespace_name, nvs_open_mode_t open_mode, nvs_handle_t *out_handle)
{
    (void)part_name;
    return abrir(namespace_name, open_mode, out_handle);
}
void nvs_close(nvs_handle_t handle) { abertos.erase(handle); }
esp_err_t nvs_commit(nvs_handle_t handle) { return achar(handle) ? ESP_OK : ESP_ERR_NVS_INVALID_HANDLE; }

CS_NUMERO(i8, int8_t, NVS_TYPE_I8)
CS_NUMERO(u8, uint8_t, NVS_TYPE_U8)
CS_NUMERO(i16, int16_t, NVS_TYPE_I16)
CS_NUMERO(u16, uint16_t, NVS_TYPE_U16)
CS_NUMERO(i32, int32_t, NVS_TYPE_I32)
CS_NUMERO(u32, uint32_t, NVS_TYPE_U32)
CS_NUMERO(i64, int64_t, NVS_TYPE_I64)
CS_NUMERO(u64, uint64_t, NVS_TYPE_U64)

esp_err_t nvs_set_str(nvs_handle_t h, const char *k, const char *valor)
{
    if (!valor) return ESP_ERR_INVALID_ARG;
    if (strlen(valor) + 1 > 4000) return ESP_ERR_NVS_VALUE_TOO_LONG;
    return gravar(h, k, NVS_TYPE_STR, valor, strlen(valor) + 1);
}
esp_err_t nvs_get_str(nvs_handle_t h, const char *k, char *saida, size_t *tamanho) { return lerVariavel(h, k, NVS_TYPE_STR, saida, tamanho); }
esp_err_t nvs_set_blob(nvs_handle_t h, const char *k, const void *valor, size_t n)
{
    if (!valor && n) return ESP_ERR_INVALID_ARG;
    if (n > 508000) return ESP_ERR_NVS_VALUE_TOO_LONG;
    return gravar(h, k, NVS_TYPE_BLOB, valor, n);
}
esp_err_t nvs_get_blob(nvs_handle_t h, const char *k, void *saida, size_t *tamanho) { return lerVariavel(h, k, NVS_TYPE_BLOB, saida, tamanho); }
esp_err_t nvs_find_key(nvs_handle_t h, const char *k, nvs_type_t *tipo)
{
    Aberto *a = achar(h);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (!chaveValida(k)) return ESP_ERR_NVS_INVALID_NAME;
    int t = tipoNaFlash(a->espaco, k);
    if (t < 0) return ESP_ERR_NVS_NOT_FOUND;
    if (tipo) *tipo = (nvs_type_t)t;
    return ESP_OK;
}
esp_err_t nvs_erase_key(nvs_handle_t h, const char *k)
{
    Aberto *a = achar(h);
    esp_err_t e = podeEscrever(a, k);
    if (e != ESP_OK) return e;
    return cs_obj_i(nullptr, "__nvs_apagar", a->espaco.c_str(), k) == 0 ? ESP_OK : ESP_ERR_NVS_NOT_FOUND;
}
esp_err_t nvs_erase_all(nvs_handle_t h)
{
    Aberto *a = achar(h);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (a->soLeitura) return ESP_ERR_NVS_READ_ONLY;
    for (const std::string &k : chavesDe(a->espaco)) cs_obj_i(nullptr, "__nvs_apagar", a->espaco.c_str(), k.c_str());
    return ESP_OK;
}
esp_err_t nvs_get_stats(const char *part_name, nvs_stats_t *stats)
{
    (void)part_name;
    if (!stats) return ESP_ERR_INVALID_ARG;
    size_t usadas = (size_t)cs_obj_i(nullptr, "__nvs_contar");
    size_t espacos = (size_t)cs_obj_i(nullptr, "__nvs_espacos");
    stats->used_entries = usadas;
    stats->free_entries = usadas >= CHAVES_NA_FLASH ? 0 : CHAVES_NA_FLASH - usadas;
    stats->available_entries = stats->free_entries;
    stats->total_entries = CHAVES_NA_FLASH;
    stats->namespace_count = espacos;
    return ESP_OK;
}
esp_err_t nvs_get_used_entry_count(nvs_handle_t h, size_t *n)
{
    Aberto *a = achar(h);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (!n) return ESP_ERR_INVALID_ARG;
    *n = chavesDe(a->espaco).size();
    return ESP_OK;
}
esp_err_t nvs_entry_find(const char *part_name, const char *namespace_name, nvs_type_t type, nvs_iterator_t *output_iterator)
{
    (void)part_name;
    if (!output_iterator || !namespace_name) return ESP_ERR_INVALID_ARG;
    return iterar(namespace_name, type, output_iterator);
}
esp_err_t nvs_entry_find_in_handle(nvs_handle_t handle, nvs_type_t type, nvs_iterator_t *output_iterator)
{
    Aberto *a = achar(handle);
    if (!a) return ESP_ERR_NVS_INVALID_HANDLE;
    if (!output_iterator) return ESP_ERR_INVALID_ARG;
    return iterar(a->espaco, type, output_iterator);
}
esp_err_t nvs_entry_next(nvs_iterator_t *iterator)
{
    if (!iterator || !*iterator) return ESP_ERR_INVALID_ARG;
    if (++(*iterator)->pos >= (*iterator)->entradas.size()) {
        delete *iterator;
        *iterator = nullptr;
        return ESP_ERR_NVS_NOT_FOUND;
    }
    return ESP_OK;
}
esp_err_t nvs_entry_info(const nvs_iterator_t iterator, nvs_entry_info_t *out_info)
{
    if (!iterator || !out_info) return ESP_ERR_INVALID_ARG;
    *out_info = iterator->entradas[iterator->pos];
    return ESP_OK;
}
void nvs_release_iterator(nvs_iterator_t iterator) { delete iterator; }

} /* extern "C" */
#endif
