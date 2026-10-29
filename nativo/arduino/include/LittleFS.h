/* LittleFS.h -- o LittleFS da ESP32: os arquivos na flash da placa (ver
 * cs_flashfs.h). */
#ifndef _LITTLEFS_H_
#define _LITTLEFS_H_

#include "cs_flashfs.h"

namespace fs {
class LittleFSFS : public CsFlashFS {
public:
    LittleFSFS() : CsFlashFS("LittleFS", "/littlefs") {}
    bool begin(bool formatOnFail = false, const char *basePath = "/littlefs", uint8_t maxOpenFiles = 10,
               const char *partitionLabel = "spiffs")
    {
        (void)maxOpenFiles;
        (void)partitionLabel;
        cs_base = basePath;
        return cs_begin(formatOnFail);
    }
};
}  // namespace fs

inline fs::LittleFSFS LittleFS;

#endif
