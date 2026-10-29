/* cs_flashfs.h -- o sistema de arquivos da flash da ESP32 (o LittleFS, o
 * SPIFFS e o FFat): o fs::FS do SD.h, com os arquivos na flash da placa,
 * que ficam de uma rodada para a outra (Maquina.SistemaDaFlash). Como no
 * core, a particao nunca formatada nao monta com begin(false). */
#ifndef CS_FLASHFS_H
#define CS_FLASHFS_H

#include "FS.h"

class CsFlashFS : public fs::FS {
protected:
    const char *cs_base;

public:
    CsFlashFS(const char *alvo, const char *base) : fs::FS(alvo), cs_base(base) {}
    bool cs_begin(bool formatOnFail) { return cs_obj_i(cs_alvo, "begin", formatOnFail) != 0; }
    bool format() { return cs_obj_i(cs_alvo, "format") != 0; }
    size_t totalBytes() { return (size_t)cs_obj_i(cs_alvo, "totalBytes"); }
    size_t usedBytes() { return (size_t)cs_obj_i(cs_alvo, "usedBytes"); }
    void end() { cs_obj_n(cs_alvo, "end"); }
    const char *mountpoint() { return cs_base; }
};

#endif
