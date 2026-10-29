/* cs_esp.h -- o objeto ESP das placas ESP32 e ESP8266 (ESP.restart(),
 * ESP.getFreeHeap()...), atendido pelo simulador. */
#ifndef CS_ESP_H
#define CS_ESP_H

#if defined(ESP32) || defined(ESP8266)
#include <stdint.h>

#include "WString.h"

typedef enum { FM_QIO = 0x00, FM_QOUT = 0x01, FM_DIO = 0x02, FM_DOUT = 0x03, FM_FAST_READ = 0x04, FM_SLOW_READ = 0x05, FM_UNKNOWN = 0xff } FlashMode_t;
typedef enum { SKETCH_SIZE_TOTAL = 0, SKETCH_SIZE_FREE = 1 } sketchSize_t;

#ifdef ESP32
#include "esp_partition.h"
#endif

class EspClass {
public:
    void restart();
    void reset() { restart(); }
    uint32_t getFreeHeap();
    uint32_t getHeapSize();
    uint32_t getMinFreeHeap();
    uint32_t getMaxAllocHeap();
    uint32_t getPsramSize();
    uint32_t getFreePsram();
    uint32_t getCpuFreqMHz();
    uint32_t getFlashChipSize();
    uint32_t getSketchSize();
    uint32_t getFreeSketchSpace();
    uint64_t getEfuseMac();
    uint32_t getChipId();
    const char *getChipModel();
    uint8_t getChipRevision();
    uint8_t getChipCores();
    const char *getSdkVersion();
    uint32_t getCycleCount();
    void deepSleep(uint64_t time_us);
#ifdef ESP32
    /* O resto do Esp.h do core 3.x. */
    uint32_t getMaxAllocPsram() { return getFreePsram(); }
    uint32_t getMinFreePsram() { return getFreePsram(); }
    uint32_t getCpuFrequencyMhz() { return getCpuFreqMHz(); }
    const char *getCoreVersion() { return "3.3.12"; }
    uint32_t getFlashChipSpeed() { return 80000000; }
    FlashMode_t getFlashChipMode() { return FM_QIO; }
    uint32_t getFlashFrequencyMHz() { return 80; }
    uint32_t getFlashSourceFrequencyMHz() { return 80; }
    uint8_t getFlashClockDivider() { return 1; }
    static uint32_t magicFlashChipSize(uint8_t flashByte)
    {
        static const uint32_t t[] = {1, 2, 4, 8, 16, 32, 64, 128};
        return flashByte < 8 ? t[flashByte] * 1024 * 1024 : 0;
    }
    static uint32_t magicFlashChipSpeed(uint8_t flashByte)
    {
        switch (flashByte & 0x0F) {
        case 0x0: return 40000000;
        case 0x1: return 26000000;
        case 0x2: return 20000000;
        case 0xf: return 80000000;
        default: return 0;
        }
    }
    static FlashMode_t magicFlashChipMode(uint8_t flashByte) { return flashByte <= FM_SLOW_READ ? (FlashMode_t)flashByte : FM_UNKNOWN; }
    String getSketchMD5();
    bool flashEraseSector(uint32_t sector);
    bool flashWrite(uint32_t offset, uint32_t *data, size_t size);
    bool flashRead(uint32_t offset, uint32_t *data, size_t size);
    bool partitionEraseRange(const esp_partition_t *partition, uint32_t offset, size_t size);
    bool partitionWrite(const esp_partition_t *partition, uint32_t offset, uint32_t *data, size_t size);
    bool partitionRead(const esp_partition_t *partition, uint32_t offset, uint32_t *data, size_t size);
#endif
};

extern EspClass ESP;

#include <time.h>
/* O relogio pela internet (NTP): o configTime e o getLocalTime do core. */
void configTime(long gmtOffset_sec, int daylightOffset_sec, const char *server1, const char *server2 = nullptr,
                const char *server3 = nullptr);
void configTzTime(const char *tz, const char *server1, const char *server2 = nullptr, const char *server3 = nullptr);
bool getLocalTime(struct tm *info, uint32_t ms = 5000);
#endif

#endif
