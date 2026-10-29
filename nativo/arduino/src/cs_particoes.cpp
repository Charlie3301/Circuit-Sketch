/* cs_particoes.cpp -- as particoes da flash e o OTA do ESP-IDF
 * (esp_partition.h, esp_ota_ops.h, esp_app_desc.h, os cabecalhos de verdade).
 *
 * A tabela e a do esquema padrao do Arduino para 4 MB (default.csv): nvs,
 * otadata, app0, app1, spiffs e coredump. O que se grava numa particao fica
 * na memoria desta rodada. O OTA confere como o IDF (o byte magico 0xE9 da
 * imagem), troca a particao de boot -- e, como no Update.h, a placa simulada
 * continua com o sketch do editor (o binario e codigo da ESP32). */
#include "Arduino.h"
#if defined(ESP32)
#include "esp_partition.h"
#include "esp_ota_ops.h"
#include "esp_app_desc.h"
#include "SHA2Builder.h"
#include "MD5Builder.h"
#include <vector>
#include <map>

namespace {

esp_partition_t tabela[] = {
    {nullptr, ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_NVS, 0x9000, 0x5000, 0x1000, "nvs", false, false},
    {nullptr, ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_OTA, 0xe000, 0x2000, 0x1000, "otadata", false, false},
    {nullptr, ESP_PARTITION_TYPE_APP, ESP_PARTITION_SUBTYPE_APP_OTA_0, 0x10000, 0x140000, 0x1000, "app0", false, false},
    {nullptr, ESP_PARTITION_TYPE_APP, ESP_PARTITION_SUBTYPE_APP_OTA_1, 0x150000, 0x140000, 0x1000, "app1", false, false},
    {nullptr, ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_SPIFFS, 0x290000, 0x160000, 0x1000, "spiffs", false, false},
    {nullptr, ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_COREDUMP, 0x3F0000, 0x10000, 0x1000, "coredump", false, false},
};
const int N = sizeof(tabela) / sizeof(tabela[0]);

std::map<const esp_partition_t *, std::vector<uint8_t>> *conteudo;

std::vector<uint8_t> &dados(const esp_partition_t *p)
{
    if (!conteudo) conteudo = new std::map<const esp_partition_t *, std::vector<uint8_t>>();
    auto &v = (*conteudo)[p];
    if (v.empty()) v.assign(p->size, 0xFF);
    return v;
}

bool combina(const esp_partition_t &p, esp_partition_type_t tipo, esp_partition_subtype_t sub, const char *rotulo)
{
    if (tipo != ESP_PARTITION_TYPE_ANY && p.type != tipo) return false;
    if (sub != ESP_PARTITION_SUBTYPE_ANY && p.subtype != sub) return false;
    if (rotulo && strcmp(p.label, rotulo) != 0) return false;
    return true;
}

struct Iterador {
    int i;
    esp_partition_type_t tipo;
    esp_partition_subtype_t sub;
    char rotulo[17];
    bool comRotulo;
};

int proximo(int de, esp_partition_type_t tipo, esp_partition_subtype_t sub, const char *rotulo)
{
    for (int i = de; i < N; i++)
        if (combina(tabela[i], tipo, sub, rotulo)) return i;
    return -1;
}

}  // namespace

/* =====================================================================
 * as particoes */
extern "C" esp_partition_iterator_t esp_partition_find(esp_partition_type_t type, esp_partition_subtype_t subtype, const char *label)
{
    int i = proximo(0, type, subtype, label);
    if (i < 0) return nullptr;
    auto *it = new Iterador{i, type, subtype, {0}, label != nullptr};
    if (label) snprintf(it->rotulo, sizeof(it->rotulo), "%s", label);
    return (esp_partition_iterator_t)it;
}

extern "C" const esp_partition_t *esp_partition_find_first(esp_partition_type_t type, esp_partition_subtype_t subtype, const char *label)
{
    int i = proximo(0, type, subtype, label);
    return i < 0 ? nullptr : &tabela[i];
}

extern "C" const esp_partition_t *esp_partition_get(esp_partition_iterator_t iterator)
{
    auto *it = (Iterador *)iterator;
    return it ? &tabela[it->i] : nullptr;
}

extern "C" esp_partition_iterator_t esp_partition_next(esp_partition_iterator_t iterator)
{
    auto *it = (Iterador *)iterator;
    if (!it) return nullptr;
    int i = proximo(it->i + 1, it->tipo, it->sub, it->comRotulo ? it->rotulo : nullptr);
    if (i < 0) {
        delete it;
        return nullptr;
    }
    it->i = i;
    return iterator;
}

extern "C" void esp_partition_iterator_release(esp_partition_iterator_t iterator) { delete (Iterador *)iterator; }

extern "C" const esp_partition_t *esp_partition_verify(const esp_partition_t *partition)
{
    for (int i = 0; i < N; i++)
        if (partition && tabela[i].address == partition->address && tabela[i].size == partition->size) return &tabela[i];
    return nullptr;
}

extern "C" esp_err_t esp_partition_read(const esp_partition_t *p, size_t src_offset, void *dst, size_t size)
{
    if (!p || !dst) return ESP_ERR_INVALID_ARG;
    if (src_offset > p->size || size > p->size - src_offset) return ESP_ERR_INVALID_SIZE;
    memcpy(dst, dados(p).data() + src_offset, size);
    return ESP_OK;
}

extern "C" esp_err_t esp_partition_write(const esp_partition_t *p, size_t dst_offset, const void *src, size_t size)
{
    if (!p || !src) return ESP_ERR_INVALID_ARG;
    if (p->readonly) return ESP_ERR_NOT_ALLOWED;
    if (dst_offset > p->size || size > p->size - dst_offset) return ESP_ERR_INVALID_SIZE;
    /* A flash so troca 1 por 0: gravar por cima sem apagar e um E logico. */
    uint8_t *d = dados(p).data() + dst_offset;
    const uint8_t *s = (const uint8_t *)src;
    for (size_t i = 0; i < size; i++) d[i] &= s[i];
    return ESP_OK;
}

extern "C" esp_err_t esp_partition_read_raw(const esp_partition_t *p, size_t src_offset, void *dst, size_t size)
{
    return esp_partition_read(p, src_offset, dst, size);
}
extern "C" esp_err_t esp_partition_write_raw(const esp_partition_t *p, size_t dst_offset, const void *src, size_t size)
{
    return esp_partition_write(p, dst_offset, src, size);
}

extern "C" esp_err_t esp_partition_erase_range(const esp_partition_t *p, size_t offset, size_t size)
{
    if (!p) return ESP_ERR_INVALID_ARG;
    if (p->readonly) return ESP_ERR_NOT_ALLOWED;
    if (offset % p->erase_size || size % p->erase_size) return ESP_ERR_INVALID_ARG;
    if (offset > p->size || size > p->size - offset) return ESP_ERR_INVALID_SIZE;
    memset(dados(p).data() + offset, 0xFF, size);
    return ESP_OK;
}

extern "C" esp_err_t esp_partition_mmap(const esp_partition_t *p, size_t offset, size_t size, esp_partition_mmap_memory_t memory,
                                        const void **out_ptr, esp_partition_mmap_handle_t *out_handle)
{
    (void)memory;
    if (!p || !out_ptr) return ESP_ERR_INVALID_ARG;
    if (offset > p->size || size > p->size - offset) return ESP_ERR_INVALID_SIZE;
    *out_ptr = dados(p).data() + offset;
    if (out_handle) *out_handle = 1;
    return ESP_OK;
}
extern "C" void esp_partition_munmap(esp_partition_mmap_handle_t handle) { (void)handle; }

extern "C" esp_err_t esp_partition_get_sha256(const esp_partition_t *p, uint8_t *sha_256)
{
    if (!p || !sha_256) return ESP_ERR_INVALID_ARG;
    SHA256Builder sha;
    sha.begin();
    sha.add(dados(p).data(), p->size);
    sha.calculate();
    sha.getBytes(sha_256);
    return ESP_OK;
}

extern "C" bool esp_partition_check_identity(const esp_partition_t *a, const esp_partition_t *b)
{
    return a && b && a->address == b->address && a->size == b->size && a->type == b->type && a->subtype == b->subtype;
}

/* =====================================================================
 * o OTA */
namespace {
const esp_partition_t *boot = &tabela[2];
struct Ota {
    const esp_partition_t *p;
    size_t escritos;
    bool magica;
    bool vivo;
};
std::vector<Ota> *otas;
esp_app_desc_t descricao;
}  // namespace

extern "C" const esp_partition_t *esp_ota_get_running_partition(void) { return &tabela[2]; }
extern "C" const esp_partition_t *esp_ota_get_boot_partition(void) { return boot; }
extern "C" const esp_partition_t *esp_ota_get_next_update_partition(const esp_partition_t *start_from)
{
    const esp_partition_t *de = start_from ? start_from : esp_ota_get_running_partition();
    return de == &tabela[2] ? &tabela[3] : &tabela[2];
}

extern "C" esp_err_t esp_ota_begin(const esp_partition_t *partition, size_t image_size, esp_ota_handle_t *out_handle)
{
    if (!partition || !out_handle) return ESP_ERR_INVALID_ARG;
    if (partition->type != ESP_PARTITION_TYPE_APP) return ESP_ERR_INVALID_ARG;
    if (partition == esp_ota_get_running_partition()) return ESP_ERR_OTA_PARTITION_CONFLICT;
    if (image_size != OTA_SIZE_UNKNOWN && image_size != OTA_WITH_SEQUENTIAL_WRITES && image_size > partition->size)
        return ESP_ERR_INVALID_SIZE;
    esp_partition_erase_range(partition, 0, partition->size);
    if (!otas) otas = new std::vector<Ota>();
    otas->push_back({partition, 0, false, true});
    *out_handle = (esp_ota_handle_t)otas->size();
    return ESP_OK;
}

static Ota *otaDe(esp_ota_handle_t h) { return otas && h >= 1 && h <= otas->size() && (*otas)[h - 1].vivo ? &(*otas)[h - 1] : nullptr; }

extern "C" esp_err_t esp_ota_write(esp_ota_handle_t handle, const void *data, size_t size)
{
    Ota *o = otaDe(handle);
    if (!o || !data) return ESP_ERR_INVALID_ARG;
    if (o->escritos == 0 && size > 0) {
        if (((const uint8_t *)data)[0] != 0xE9) {
            log_e("OTA image has invalid magic byte (expected 0xE9, saw 0x%02x)", ((const uint8_t *)data)[0]);
            return ESP_ERR_OTA_VALIDATE_FAILED;
        }
        o->magica = true;
    }
    esp_err_t r = esp_partition_write(o->p, o->escritos, data, size);
    if (r == ESP_OK) o->escritos += size;
    return r;
}

extern "C" esp_err_t esp_ota_write_with_offset(esp_ota_handle_t handle, const void *data, size_t size, uint32_t offset)
{
    Ota *o = otaDe(handle);
    if (!o) return ESP_ERR_INVALID_ARG;
    if (offset == 0 && size > 0) o->magica = ((const uint8_t *)data)[0] == 0xE9;
    esp_err_t r = esp_partition_write(o->p, offset, data, size);
    if (r == ESP_OK && offset + size > o->escritos) o->escritos = offset + size;
    return r;
}

extern "C" esp_err_t esp_ota_end(esp_ota_handle_t handle)
{
    Ota *o = otaDe(handle);
    if (!o) return ESP_ERR_NOT_FOUND;
    o->vivo = false;
    if (o->escritos == 0 || !o->magica) return ESP_ERR_OTA_VALIDATE_FAILED;
    return ESP_OK;
}

extern "C" esp_err_t esp_ota_abort(esp_ota_handle_t handle)
{
    Ota *o = otaDe(handle);
    if (!o) return ESP_ERR_NOT_FOUND;
    o->vivo = false;
    return ESP_OK;
}

extern "C" esp_err_t esp_ota_set_boot_partition(const esp_partition_t *partition)
{
    if (!partition || partition->type != ESP_PARTITION_TYPE_APP) return ESP_ERR_INVALID_ARG;
    boot = partition;
    if (partition != esp_ota_get_running_partition())
        log_printf("[OTA] a particao de boot agora e a %s. A placa simulada continua com o sketch do editor depois de reiniciar: "
                   "o binario e codigo da ESP32, que o simulador nao executa.\n",
                   partition->label);
    return ESP_OK;
}

extern "C" const esp_app_desc_t *esp_app_get_description(void)
{
    if (!descricao.magic_word) {
        descricao.magic_word = ESP_APP_DESC_MAGIC_WORD;
        snprintf(descricao.version, sizeof(descricao.version), "1");
        snprintf(descricao.project_name, sizeof(descricao.project_name), "sketch");
        snprintf(descricao.time, sizeof(descricao.time), "%s", __TIME__);
        snprintf(descricao.date, sizeof(descricao.date), "%s", __DATE__);
        snprintf(descricao.idf_ver, sizeof(descricao.idf_ver), "v5.5.1");
    }
    return &descricao;
}

extern "C" esp_err_t esp_ota_get_partition_description(const esp_partition_t *partition, esp_app_desc_t *app_desc)
{
    if (!partition || !app_desc) return ESP_ERR_INVALID_ARG;
    if (partition != esp_ota_get_running_partition()) return ESP_ERR_NOT_FOUND;
    *app_desc = *esp_app_get_description();
    return ESP_OK;
}

extern "C" esp_err_t esp_ota_mark_app_valid_cancel_rollback(void) { return ESP_OK; }
extern "C" esp_err_t esp_ota_mark_app_invalid_rollback(void) { return ESP_FAIL; }
extern "C" esp_err_t esp_ota_mark_app_invalid_rollback_and_reboot(void) { return ESP_FAIL; }
extern "C" const esp_partition_t *esp_ota_get_last_invalid_partition(void) { return nullptr; }
extern "C" esp_err_t esp_ota_get_state_partition(const esp_partition_t *partition, esp_ota_img_states_t *ota_state)
{
    if (!partition || !ota_state) return ESP_ERR_INVALID_ARG;
    *ota_state = partition == esp_ota_get_running_partition() ? ESP_OTA_IMG_VALID : ESP_OTA_IMG_UNDEFINED;
    return ESP_OK;
}
extern "C" esp_err_t esp_ota_erase_last_boot_app_partition(void) { return ESP_OK; }
extern "C" bool esp_ota_check_rollback_is_possible(void) { return false; }
extern "C" esp_err_t esp_ota_invalidate_inactive_ota_data_slot(void) { return ESP_OK; }

/* =====================================================================
 * o ESP.flash* e o ESP.partition* (Esp.h) */
static const esp_partition_t *daFlash(uint32_t endereco)
{
    for (int i = 0; i < N; i++)
        if (endereco >= tabela[i].address && endereco < tabela[i].address + tabela[i].size) return &tabela[i];
    return nullptr;
}

bool EspClass::flashEraseSector(uint32_t sector)
{
    const esp_partition_t *p = daFlash(sector * 4096);
    return p && esp_partition_erase_range(p, sector * 4096 - p->address, 4096) == ESP_OK;
}
bool EspClass::flashWrite(uint32_t offset, uint32_t *data, size_t size)
{
    const esp_partition_t *p = daFlash(offset);
    return p && esp_partition_write(p, offset - p->address, data, size) == ESP_OK;
}
bool EspClass::flashRead(uint32_t offset, uint32_t *data, size_t size)
{
    const esp_partition_t *p = daFlash(offset);
    return p && esp_partition_read(p, offset - p->address, data, size) == ESP_OK;
}
bool EspClass::partitionEraseRange(const esp_partition_t *partition, uint32_t offset, size_t size)
{
    return esp_partition_erase_range(partition, offset, size) == ESP_OK;
}
bool EspClass::partitionWrite(const esp_partition_t *partition, uint32_t offset, uint32_t *data, size_t size)
{
    return esp_partition_write(partition, offset, data, size) == ESP_OK;
}
bool EspClass::partitionRead(const esp_partition_t *partition, uint32_t offset, uint32_t *data, size_t size)
{
    return esp_partition_read(partition, offset, data, size) == ESP_OK;
}

/* O MD5 do firmware que esta rodando: o do sketch compilado (a data e a hora
 * da compilacao o identificam). */
String EspClass::getSketchMD5()
{
    static String guardado;
    if (!guardado.length()) {
        const esp_app_desc_t *d = esp_app_get_description();
        MD5Builder md5;
        md5.begin();
        md5.add(String(d->project_name) + d->date + d->time);
        md5.calculate();
        guardado = md5.toString();
    }
    return guardado;
}

#endif
