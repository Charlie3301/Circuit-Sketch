/* cs_esp.h -- o objeto ESP das placas ESP32 e ESP8266 (ESP.restart(),
 * ESP.getFreeHeap()...), atendido pelo simulador. */
#ifndef CS_ESP_H
#define CS_ESP_H

#if defined(ESP32) || defined(ESP8266)
#include <stdint.h>

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
