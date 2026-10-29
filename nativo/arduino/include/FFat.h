/* FFat.h -- o FAT na flash da ESP32: os arquivos na flash da placa (ver
 * cs_flashfs.h). */
#ifndef _FFAT_H_
#define _FFAT_H_

#include "cs_flashfs.h"

#define FFAT_WIPE_QUICK 0
#define FFAT_WIPE_FULL 1
#define FFAT_PARTITION_LABEL "ffat"

namespace fs {
class F_Fat : public CsFlashFS {
public:
    F_Fat() : CsFlashFS("FFat", "/ffat") {}
    bool begin(bool formatOnFail = false, const char *basePath = "/ffat", uint8_t maxOpenFiles = 10,
               const char *partitionLabel = (char *)FFAT_PARTITION_LABEL)
    {
        (void)maxOpenFiles;
        (void)partitionLabel;
        cs_base = basePath;
        return cs_begin(formatOnFail);
    }
    bool format(bool full_wipe = FFAT_WIPE_QUICK, char *partitionLabel = (char *)FFAT_PARTITION_LABEL)
    {
        (void)full_wipe;
        (void)partitionLabel;
        return CsFlashFS::format();
    }
    size_t freeBytes() { return (size_t)cs_obj_i(cs_alvo, "freeBytes"); }
};
}  // namespace fs

inline fs::F_Fat FFat;

#endif
