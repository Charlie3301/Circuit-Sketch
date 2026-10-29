/* SPIFFS.h -- o SPIFFS da ESP32: os arquivos na flash da placa (ver
 * cs_flashfs.h). */
#ifndef _SPIFFS_H_
#define _SPIFFS_H_

#include "cs_flashfs.h"

namespace fs {
class SPIFFSFS : public CsFlashFS {
public:
    SPIFFSFS() : CsFlashFS("SPIFFS", "/spiffs") {}
    bool begin(bool formatOnFail = false, const char *basePath = "/spiffs", uint8_t maxOpenFiles = 10, const char *partitionLabel = NULL)
    {
        (void)maxOpenFiles;
        (void)partitionLabel;
        cs_base = basePath;
        return cs_begin(formatOnFail);
    }
};
}  // namespace fs

inline fs::SPIFFSFS SPIFFS;

#endif
