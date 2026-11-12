/* DallasTemperature.h -- os termometros de 1-Wire (a biblioteca de Miles Burton).
 *
 * A mesma logica da biblioteca, sobre o OneWire do simulador: a busca dos
 * aparelhos no begin(), o pedido de conversao (com a espera do tempo da
 * resolucao), o scratchpad com o CRC conferido, os -127 graus de quando o
 * sensor nao responde (o fio solto, o pull-up que falta) e os 85 de quando se
 * le sem ter pedido a conversao -- que vem do chip, e nao daqui. */
#ifndef DallasTemperature_h
#define DallasTemperature_h

#define DALLASTEMPLIBVERSION "4.0.5"

#include <Arduino.h>
#include <OneWire.h>

#define DS18S20MODEL 0x10
#define DS18B20MODEL 0x28
#define DS1822MODEL 0x22
#define DS1825MODEL 0x3B
#define DS28EA00MODEL 0x42

#define STARTCONVO 0x44
#define COPYSCRATCH 0x48
#define READSCRATCH 0xBE
#define WRITESCRATCH 0x4E
#define RECALLSCRATCH 0xB8
#define READPOWERSUPPLY 0xB4
#define ALARMSEARCH 0xEC

#define TEMP_LSB 0
#define TEMP_MSB 1
#define HIGH_ALARM_TEMP 2
#define LOW_ALARM_TEMP 3
#define CONFIGURATION 4
#define INTERNAL_BYTE 5
#define COUNT_REMAIN 6
#define COUNT_PER_C 7
#define SCRATCHPAD_CRC 8

#define TEMP_9_BIT 0x1F
#define TEMP_10_BIT 0x3F
#define TEMP_11_BIT 0x5F
#define TEMP_12_BIT 0x7F

#define MAX_CONVERSION_TIMEOUT 750

#define DEVICE_DISCONNECTED_C -127
#define DEVICE_DISCONNECTED_F -196.6
#define DEVICE_DISCONNECTED_RAW -7040

typedef uint8_t DeviceAddress[8];

class DallasTemperature {
public:
    typedef uint8_t ScratchPad[9];

    struct request_t {
        bool result;
        unsigned long timestamp;
        operator bool() { return result; }
    };

    DallasTemperature() {}
    DallasTemperature(OneWire *w) { setOneWire(w); }
    DallasTemperature(OneWire *w, uint8_t pullupPin) { setOneWire(w); (void)pullupPin; }

    void setOneWire(OneWire *w)
    {
        _wire = w;
        devices = 0;
        ds18Count = 0;
        parasite = false;
        bitResolution = 9;
        waitForConversion = true;
        checkForConversion = true;
        autoSaveScratchPad = true;
    }
    void setPullupPin(uint8_t) {}

    void begin(void)
    {
        DeviceAddress deviceAddress;
        _wire->reset_search();
        devices = 0;
        ds18Count = 0;
        while (_wire->search(deviceAddress)) {
            if (validAddress(deviceAddress)) {
                devices++;
                if (validFamily(deviceAddress)) {
                    ds18Count++;
                    if (!parasite && readPowerSupply(deviceAddress)) parasite = true;
                    uint8_t b = getResolution(deviceAddress);
                    if (b > bitResolution) bitResolution = b;
                }
            }
        }
    }

    uint8_t getDeviceCount(void) { return devices; }
    uint8_t getDS18Count(void) { return ds18Count; }
    bool validAddress(const uint8_t *deviceAddress) { return OneWire::crc8(deviceAddress, 7) == deviceAddress[7]; }
    bool validFamily(const uint8_t *deviceAddress)
    {
        switch (deviceAddress[0]) {
        case DS18S20MODEL: case DS18B20MODEL: case DS1822MODEL: case DS1825MODEL: case DS28EA00MODEL: return true;
        default: return false;
        }
    }

    bool getAddress(uint8_t *deviceAddress, uint8_t index)
    {
        uint8_t depth = 0;
        _wire->reset_search();
        while (depth <= index && _wire->search(deviceAddress)) {
            if (depth == index && validAddress(deviceAddress)) return true;
            depth++;
        }
        return false;
    }

    bool isConnected(const uint8_t *deviceAddress)
    {
        ScratchPad scratchPad;
        return isConnected(deviceAddress, scratchPad);
    }
    bool isConnected(const uint8_t *deviceAddress, uint8_t *scratchPad)
    {
        bool b = readScratchPad(deviceAddress, scratchPad);
        return b && !isAllZeros(scratchPad) && OneWire::crc8(scratchPad, 8) == scratchPad[SCRATCHPAD_CRC];
    }

    bool readScratchPad(const uint8_t *deviceAddress, uint8_t *scratchPad)
    {
        if (_wire->reset() == 0) return false;
        _wire->select(deviceAddress);
        _wire->write(READSCRATCH);
        for (uint8_t i = 0; i < 9; i++) scratchPad[i] = _wire->read();
        return _wire->reset() == 1;
    }

    void writeScratchPad(const uint8_t *deviceAddress, const uint8_t *scratchPad)
    {
        _wire->reset();
        _wire->select(deviceAddress);
        _wire->write(WRITESCRATCH);
        _wire->write(scratchPad[HIGH_ALARM_TEMP]);
        _wire->write(scratchPad[LOW_ALARM_TEMP]);
        if (deviceAddress[0] != DS18S20MODEL) _wire->write(scratchPad[CONFIGURATION]);
        if (autoSaveScratchPad) saveScratchPad(deviceAddress);
        else _wire->reset();
    }

    bool readPowerSupply(const uint8_t *deviceAddress = nullptr)
    {
        bool parasiteMode = false;
        _wire->reset();
        if (deviceAddress == nullptr) _wire->skip();
        else _wire->select(deviceAddress);
        _wire->write(READPOWERSUPPLY);
        if (_wire->read_bit() == 0) parasiteMode = true;
        _wire->reset();
        return parasiteMode;
    }

    uint8_t getResolution() { return bitResolution; }
    uint8_t getResolution(const uint8_t *deviceAddress)
    {
        if (deviceAddress[0] == DS18S20MODEL) return 12;
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) {
            if (deviceAddress[0] == DS1825MODEL && scratchPad[CONFIGURATION] & 0x80) return 12;
            switch (scratchPad[CONFIGURATION]) {
            case TEMP_12_BIT: return 12;
            case TEMP_11_BIT: return 11;
            case TEMP_10_BIT: return 10;
            case TEMP_9_BIT: return 9;
            }
        }
        return 0;
    }

    void setResolution(uint8_t newResolution)
    {
        bitResolution = constrain(newResolution, 9, 12);
        DeviceAddress deviceAddress;
        _wire->reset_search();
        for (uint8_t i = 0; i < devices; i++)
            if (_wire->search(deviceAddress) && validAddress(deviceAddress)) setResolution(deviceAddress, bitResolution, true);
    }
    bool setResolution(const uint8_t *deviceAddress, uint8_t newResolution, bool skipGlobalBitResolutionCalculation = false)
    {
        bool success = false;
        if (deviceAddress[0] == DS18S20MODEL) success = true;
        else {
            newResolution = constrain(newResolution, 9, 12);
            uint8_t newValue = 0;
            ScratchPad scratchPad;
            if (isConnected(deviceAddress, scratchPad)) {
                switch (newResolution) {
                case 12: newValue = TEMP_12_BIT; break;
                case 11: newValue = TEMP_11_BIT; break;
                case 10: newValue = TEMP_10_BIT; break;
                default: newValue = TEMP_9_BIT; break;
                }
                if (scratchPad[CONFIGURATION] != newValue) {
                    scratchPad[CONFIGURATION] = newValue;
                    writeScratchPad(deviceAddress, scratchPad);
                }
                success = true;
            }
        }
        if (!skipGlobalBitResolutionCalculation && success) {
            bitResolution = newResolution;
            if (devices > 1) {
                DeviceAddress deviceAddr;
                _wire->reset_search();
                for (uint8_t i = 0; i < devices; i++) {
                    if (bitResolution == 12) break;
                    if (_wire->search(deviceAddr) && validAddress(deviceAddr)) {
                        uint8_t b = getResolution(deviceAddr);
                        if (b > bitResolution) bitResolution = b;
                    }
                }
            }
        }
        return success;
    }

    void setWaitForConversion(bool flag) { waitForConversion = flag; }
    bool getWaitForConversion(void) { return waitForConversion; }
    void setCheckForConversion(bool flag) { checkForConversion = flag; }
    bool getCheckForConversion(void) { return checkForConversion; }
    void setAutoSaveScratchPad(bool flag) { autoSaveScratchPad = flag; }
    bool getAutoSaveScratchPad(void) { return autoSaveScratchPad; }

    request_t requestTemperatures(void)
    {
        request_t req = {true, millis()};
        _wire->reset();
        _wire->skip();
        _wire->write(STARTCONVO, parasite);
        if (!waitForConversion) return req;
        blockTillConversionComplete(bitResolution, req.timestamp);
        return req;
    }
    request_t requestTemperaturesByAddress(const uint8_t *deviceAddress)
    {
        request_t req = {true, millis()};
        uint8_t deviceBitResolution = getResolution(deviceAddress);
        if (deviceBitResolution == 0) {
            req.result = false;
            return req;
        }
        _wire->reset();
        _wire->select(deviceAddress);
        _wire->write(STARTCONVO, parasite);
        if (!waitForConversion) return req;
        blockTillConversionComplete(deviceBitResolution, req.timestamp);
        return req;
    }
    request_t requestTemperaturesByIndex(uint8_t index)
    {
        DeviceAddress deviceAddress;
        getAddress(deviceAddress, index);
        return requestTemperaturesByAddress(deviceAddress);
    }

    bool isConversionComplete(void) { return _wire->read_bit() == 1; }
    static uint16_t millisToWaitForConversion(uint8_t bitResolution)
    {
        switch (bitResolution) {
        case 9: return 94;
        case 10: return 188;
        case 11: return 375;
        default: return 750;
        }
    }
    uint16_t millisToWaitForConversion() { return millisToWaitForConversion(bitResolution); }

    int32_t getTemp(const uint8_t *deviceAddress)
    {
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) return calculateTemperature(deviceAddress, scratchPad);
        return DEVICE_DISCONNECTED_RAW;
    }
    float getTempC(const uint8_t *deviceAddress) { return rawToCelsius(getTemp(deviceAddress)); }
    float getTempF(const uint8_t *deviceAddress) { return rawToFahrenheit(getTemp(deviceAddress)); }
    float getTempCByIndex(uint8_t index)
    {
        DeviceAddress deviceAddress;
        if (!getAddress(deviceAddress, index)) return DEVICE_DISCONNECTED_C;
        return getTempC(deviceAddress);
    }
    float getTempFByIndex(uint8_t index)
    {
        DeviceAddress deviceAddress;
        if (!getAddress(deviceAddress, index)) return DEVICE_DISCONNECTED_F;
        return getTempF(deviceAddress);
    }

    bool isParasitePowerMode(void) { return parasite; }

    bool saveScratchPadByIndex(uint8_t deviceIndex)
    {
        DeviceAddress deviceAddress;
        if (!getAddress(deviceAddress, deviceIndex)) return false;
        return saveScratchPad(deviceAddress);
    }
    bool saveScratchPad(const uint8_t *deviceAddress = nullptr)
    {
        if (_wire->reset() == 0) return false;
        if (deviceAddress == nullptr) _wire->skip();
        else _wire->select(deviceAddress);
        _wire->write(COPYSCRATCH, parasite);
        delay(20);
        return _wire->reset() == 1;
    }
    bool recallScratchPadByIndex(uint8_t deviceIndex)
    {
        DeviceAddress deviceAddress;
        if (!getAddress(deviceAddress, deviceIndex)) return false;
        return recallScratchPad(deviceAddress);
    }
    bool recallScratchPad(const uint8_t *deviceAddress = nullptr)
    {
        if (_wire->reset() == 0) return false;
        if (deviceAddress == nullptr) _wire->skip();
        else _wire->select(deviceAddress);
        _wire->write(RECALLSCRATCH, parasite);
        unsigned long start = millis();
        while (_wire->read_bit() == 0) {
            if (millis() - start > 20) return false;
            delay(1);
        }
        return _wire->reset() == 1;
    }

    void setHighAlarmTemp(const uint8_t *deviceAddress, int8_t celsius) { setAlarmTemp(deviceAddress, celsius, HIGH_ALARM_TEMP); }
    void setLowAlarmTemp(const uint8_t *deviceAddress, int8_t celsius) { setAlarmTemp(deviceAddress, celsius, LOW_ALARM_TEMP); }
    int8_t getHighAlarmTemp(const uint8_t *deviceAddress)
    {
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) return (int8_t)scratchPad[HIGH_ALARM_TEMP];
        return DEVICE_DISCONNECTED_C;
    }
    int8_t getLowAlarmTemp(const uint8_t *deviceAddress)
    {
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) return (int8_t)scratchPad[LOW_ALARM_TEMP];
        return DEVICE_DISCONNECTED_C;
    }
    void resetAlarmSearch(void) { _wire->reset_search(); }
    bool alarmSearch(uint8_t *newAddr) { return _wire->search(newAddr, false); }
    bool hasAlarm(const uint8_t *deviceAddress)
    {
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) {
            int8_t temp = calculateTemperature(deviceAddress, scratchPad) >> 7;
            return temp <= (int8_t)scratchPad[LOW_ALARM_TEMP] || temp >= (int8_t)scratchPad[HIGH_ALARM_TEMP];
        }
        return false;
    }
    bool hasAlarm(void)
    {
        DeviceAddress deviceAddress;
        resetAlarmSearch();
        return alarmSearch(deviceAddress);
    }

    static float toFahrenheit(float celsius) { return celsius * 1.8f + 32.0f; }
    static float toCelsius(float fahrenheit) { return (fahrenheit - 32.0f) * 0.555555556f; }
    static float rawToCelsius(int32_t raw)
    {
        if (raw <= DEVICE_DISCONNECTED_RAW) return DEVICE_DISCONNECTED_C;
        return (float)raw * 0.0078125f;
    }
    static float rawToFahrenheit(int32_t raw)
    {
        if (raw <= DEVICE_DISCONNECTED_RAW) return DEVICE_DISCONNECTED_F;
        return (float)raw * 0.0140625f + 32.0f;
    }
    static int16_t celsiusToRaw(float celsius) { return (int16_t)(celsius * 128); }

private:
    OneWire *_wire = nullptr;
    uint8_t devices = 0, ds18Count = 0;
    bool parasite = false;
    uint8_t bitResolution = 9;
    bool waitForConversion = true, checkForConversion = true, autoSaveScratchPad = true;

    static bool isAllZeros(const uint8_t *scratchPad, int length = 9)
    {
        for (int i = 0; i < length; i++)
            if (scratchPad[i] != 0) return false;
        return true;
    }

    int32_t calculateTemperature(const uint8_t *deviceAddress, uint8_t *scratchPad)
    {
        int32_t fpTemperature = (((int16_t)scratchPad[TEMP_MSB]) << 11) | (((int16_t)scratchPad[TEMP_LSB]) << 3);
        if (deviceAddress[0] == DS18S20MODEL)
            fpTemperature = ((fpTemperature & 0xfff0) << 3) - 32
                            + (((scratchPad[COUNT_PER_C] - scratchPad[COUNT_REMAIN]) << 7) / scratchPad[COUNT_PER_C]);
        return fpTemperature;
    }

    void blockTillConversionComplete(uint8_t bitResolution, unsigned long start)
    {
        if (checkForConversion && !parasite) {
            while (!isConversionComplete() && (millis() - start < MAX_CONVERSION_TIMEOUT)) delay(1);
        } else {
            unsigned long delms = millisToWaitForConversion(bitResolution);
            delay(delms);
        }
    }

    void setAlarmTemp(const uint8_t *deviceAddress, int8_t celsius, uint8_t qual)
    {
        if (celsius > 125) celsius = 125;
        else if (celsius < -55) celsius = -55;
        ScratchPad scratchPad;
        if (isConnected(deviceAddress, scratchPad)) {
            scratchPad[qual] = (uint8_t)celsius;
            writeScratchPad(deviceAddress, scratchPad);
        }
    }
};

#endif
