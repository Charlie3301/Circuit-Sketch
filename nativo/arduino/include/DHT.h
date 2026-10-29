/* DHT.h -- o DHT11/DHT22 do circuito (a "DHT sensor library" da Adafruit). */
#ifndef DHT_H
#define DHT_H

#include "cs_objeto.h"

#define DHT11 11
#define DHT12 12
#define DHT21 21
#define DHT22 22
#define AM2301 21
#define AM2302 22

class DHT : public CsObjeto {
public:
    DHT(uint8_t pin, uint8_t type, uint8_t count = 6) { (void)count; cs_criar("DHT", pin, type); }
    void begin(uint8_t usec = 55) { (void)usec; cs_obj_n(cs_nome, "begin"); }
    float readTemperature(bool S = false, bool force = false) { (void)force; return (float)cs_obj_r(cs_nome, "readTemperature", S); }
    float readHumidity(bool force = false) { (void)force; return (float)cs_obj_r(cs_nome, "readHumidity"); }
    float computeHeatIndex(bool isFahrenheit = true)
    {
        return computeHeatIndex(readTemperature(isFahrenheit), readHumidity(), isFahrenheit);
    }
    float computeHeatIndex(float temperature, float percentHumidity, bool isFahrenheit = true)
    {
        return (float)cs_obj_r(cs_nome, "computeHeatIndex", temperature, percentHumidity, isFahrenheit);
    }
    float convertCtoF(float c) { return c * 1.8f + 32; }
    float convertFtoC(float f) { return (f - 32) * 0.55555f; }
    bool read(bool force = false) { (void)force; return !isnan(readTemperature()); }
};

#endif
