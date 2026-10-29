/* SD_MMC.h -- o cartao microSD pelo barramento SDMMC da ESP32 (1 ou 4 bits):
 * o mesmo cartao do circuito que o SD.h usa, sem o SPI. */
#ifndef _SDMMC_H_
#define _SDMMC_H_

#include "SD.h"

#define SDMMC_FREQ_DEFAULT 20000
#define SDMMC_FREQ_HIGHSPEED 40000
#define SDMMC_FREQ_PROBING 400
#define SDMMC_FREQ_52M 52000
#define SDMMC_FREQ_26M 26000

class SDMMCFS : public fs::FS {
public:
    bool setPins(int clk, int cmd, int d0, int d1 = -1, int d2 = -1, int d3 = -1)
    {
        (void)clk; (void)cmd; (void)d0; (void)d1; (void)d2; (void)d3;
        return true;
    }
    bool begin(const char *mountpoint = "/sdcard", bool mode1bit = false, bool format_if_mount_failed = false,
               int sdmmc_frequency = SDMMC_FREQ_DEFAULT, uint8_t maxOpenFiles = 5)
    {
        (void)mountpoint; (void)mode1bit; (void)format_if_mount_failed; (void)sdmmc_frequency; (void)maxOpenFiles;
        return cs_int(nullptr, "__sdmmc_begin", 0, nullptr) != 0;
    }
    void end() { cs_obj_n("SD", "end"); }
    uint8_t cardType() { return CARD_SDHC; }
    uint64_t cardSize() { return 4ULL * 1024 * 1024 * 1024; }
    uint64_t totalBytes() { return cardSize(); }
    uint64_t usedBytes() { return 0; }
};

inline SDMMCFS SD_MMC;

#endif
