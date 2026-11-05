/* RTClib.h -- o relogio DS1307/DS3231 (a RTClib da Adafruit). O DateTime e
 * o TimeSpan sao contas, feitos aqui com os algoritmos da biblioteca; o
 * relogio do circuito (o que anda, o adjust, o lostPower) e o do simulador. */
#ifndef _RTCLIB_H_
#define _RTCLIB_H_

#include "cs_objeto.h"
#include "Wire.h"

#define SECONDS_PER_DAY 86400L
#define SECONDS_FROM_1970_TO_2000 946684800

class TimeSpan;

class DateTime {
public:
    DateTime(uint32_t t = SECONDS_FROM_1970_TO_2000);
    DateTime(uint16_t year, uint8_t month, uint8_t day, uint8_t hour = 0, uint8_t min = 0, uint8_t sec = 0);
    DateTime(const DateTime &copy) = default;
    DateTime &operator=(const DateTime &copy) = default;
    DateTime(const char *date, const char *time);
    DateTime(const __FlashStringHelper *date, const __FlashStringHelper *time)
        : DateTime(reinterpret_cast<const char *>(date), reinterpret_cast<const char *>(time)) {}
    DateTime(const char *iso8601date);

    bool isValid() const;
    char *toString(char *buffer) const;
    uint16_t year() const { return 2000U + yOff; }
    uint8_t month() const { return m; }
    uint8_t day() const { return d; }
    uint8_t hour() const { return hh; }
    uint8_t twelveHour() const;
    uint8_t isPM() const { return hh >= 12; }
    uint8_t minute() const { return mm; }
    uint8_t second() const { return ss; }
    uint8_t dayOfTheWeek() const;
    uint32_t secondstime() const;
    uint32_t unixtime(void) const;

    enum timestampOpt { TIMESTAMP_FULL, TIMESTAMP_TIME, TIMESTAMP_DATE };
    String timestamp(timestampOpt opt = TIMESTAMP_FULL) const;

    DateTime operator+(const TimeSpan &span) const;
    DateTime operator-(const TimeSpan &span) const;
    TimeSpan operator-(const DateTime &right) const;
    bool operator<(const DateTime &right) const { return unixtime() < right.unixtime(); }
    bool operator>(const DateTime &right) const { return right < *this; }
    bool operator<=(const DateTime &right) const { return !(*this > right); }
    bool operator>=(const DateTime &right) const { return !(*this < right); }
    bool operator==(const DateTime &right) const { return unixtime() == right.unixtime(); }
    bool operator!=(const DateTime &right) const { return !(*this == right); }

protected:
    uint8_t yOff, m, d, hh, mm, ss;
};

class TimeSpan {
public:
    TimeSpan(int32_t seconds = 0) : _seconds(seconds) {}
    TimeSpan(int16_t days, int8_t hours, int8_t minutes, int8_t seconds)
        : _seconds((int32_t)days * 86400L + (int32_t)hours * 3600 + (int32_t)minutes * 60 + seconds) {}
    int16_t days() const { return (int16_t)(_seconds / 86400L); }
    int8_t hours() const { return (int8_t)(_seconds / 3600 % 24); }
    int8_t minutes() const { return (int8_t)(_seconds / 60 % 60); }
    int8_t seconds() const { return (int8_t)(_seconds % 60); }
    int32_t totalseconds() const { return _seconds; }
    TimeSpan operator+(const TimeSpan &right) const { return TimeSpan(_seconds + right._seconds); }
    TimeSpan operator-(const TimeSpan &right) const { return TimeSpan(_seconds - right._seconds); }

protected:
    int32_t _seconds;
};

enum Ds1307SqwPinMode { DS1307_OFF = 0x00, DS1307_ON = 0x80, DS1307_SquareWave1HZ = 0x10, DS1307_SquareWave4kHz = 0x11,
                        DS1307_SquareWave8kHz = 0x12, DS1307_SquareWave32kHz = 0x13 };
enum Ds3231SqwPinMode { DS3231_OFF = 0x1C, DS3231_SquareWave1Hz = 0x00, DS3231_SquareWave1kHz = 0x08,
                        DS3231_SquareWave4kHz = 0x10, DS3231_SquareWave8kHz = 0x18 };

enum Ds3231Alarm1Mode { DS3231_A1_PerSecond = 0x0F, DS3231_A1_Second = 0x0E, DS3231_A1_Minute = 0x0C, DS3231_A1_Hour = 0x08,
                        DS3231_A1_Date = 0x00, DS3231_A1_Day = 0x10 };
enum Ds3231Alarm2Mode { DS3231_A2_PerMinute = 0x7, DS3231_A2_Minute = 0x6, DS3231_A2_Hour = 0x4, DS3231_A2_Date = 0x0, DS3231_A2_Day = 0x8 };
enum Pcf8523SqwPinMode { PCF8523_OFF = 7, PCF8523_SquareWave1HZ = 6, PCF8523_SquareWave32HZ = 5, PCF8523_SquareWave1kHz = 4,
                         PCF8523_SquareWave4kHz = 3, PCF8523_SquareWave8kHz = 2, PCF8523_SquareWave16kHz = 1, PCF8523_SquareWave32kHz = 0 };
enum PCF8523TimerClockFreq { PCF8523_Frequency4kHz = 0, PCF8523_Frequency64Hz = 1, PCF8523_FrequencySecond = 2,
                             PCF8523_FrequencyMinute = 3, PCF8523_FrequencyHour = 4 };
enum PCF8523TimerIntPulse { PCF8523_LowPulse3x64Hz = 0, PCF8523_LowPulse4x64Hz = 1, PCF8523_LowPulse5x64Hz = 2, PCF8523_LowPulse6x64Hz = 3,
                            PCF8523_LowPulse8x64Hz = 4, PCF8523_LowPulse10x64Hz = 5, PCF8523_LowPulse12x64Hz = 6, PCF8523_LowPulse14x64Hz = 7 };
enum Pcf8523OffsetMode { PCF8523_TwoHours = 0x00, PCF8523_OneMinute = 0x80 };
enum Pcf8563SqwPinMode { PCF8563_SquareWaveOFF = 0x00, PCF8563_SquareWave1Hz = 0x83, PCF8563_SquareWave32Hz = 0x82,
                         PCF8563_SquareWave1kHz = 0x81, PCF8563_SquareWave32kHz = 0x80 };

/* O relogio do circuito: o DS1307 do simulador. O DS3231 e o PCF8523 estao no mesmo endereco (0x68) e
 * contam a mesma hora; o PCF8563 e o do 0x51. O que so o chip guarda (a NVRAM, os alarmes, o timer)
 * fica aqui, no objeto. */
class RTC_DS1307 : public CsObjeto {
public:
    RTC_DS1307() { cs_criar("RTC_DS1307"); }
    /* A NVRAM de 56 bytes do DS1307 (o enderecos 0x08 a 0x3F do chip). */
    uint8_t readnvram(uint8_t address) { return address < 56 ? nvram[address] : 0; }
    void readnvram(uint8_t *buf, uint8_t size, uint8_t address)
    {
        for (uint8_t i = 0; i < size; i++) buf[i] = readnvram((uint8_t)(address + i));
    }
    void writenvram(uint8_t address, uint8_t data)
    {
        if (address < 56) nvram[address] = data;
    }
    void writenvram(uint8_t address, const uint8_t *buf, uint8_t size)
    {
        for (uint8_t i = 0; i < size; i++) writenvram((uint8_t)(address + i), buf[i]);
    }

protected:
    explicit RTC_DS1307(const char *classe) { cs_criar(classe); }

private:
    uint8_t nvram[56] = {0};

public:
    bool begin(TwoWire *wireInstance = &Wire) { return cs_obj_i(cs_nome, "begin", wireInstance) != 0; }
    void adjust(const DateTime &dt);
    uint8_t isrunning(void) { return (uint8_t)cs_obj_i(cs_nome, "isrunning"); }
    bool lostPower(void) { return cs_obj_i(cs_nome, "lostPower") != 0; }
    DateTime now();
    Ds1307SqwPinMode readSqwPinMode() { return (Ds1307SqwPinMode)cs_obj_i(cs_nome, "readSqwPinMode"); }
    void writeSqwPinMode(Ds1307SqwPinMode mode) { cs_obj_n(cs_nome, "writeSqwPinMode", (int)mode); }
    float getTemperature() { return (float)cs_obj_r(cs_nome, "getTemperature"); }
};

class RTC_DS3231 : public RTC_DS1307 {
public:
    void disable32K() { saida32k = false; }
    void enable32K() { saida32k = true; }
    bool isEnabled32K() { return saida32k; }
    bool setAlarm1(const DateTime &dt, Ds3231Alarm1Mode modo) { return armar(0, dt, (uint8_t)modo); }
    bool setAlarm2(const DateTime &dt, Ds3231Alarm2Mode modo) { return armar(1, dt, (uint8_t)modo); }
    DateTime getAlarm1() { return alarme[0].quando; }
    DateTime getAlarm2() { return alarme[1].quando; }
    Ds3231Alarm1Mode getAlarm1Mode() { return (Ds3231Alarm1Mode)alarme[0].modo; }
    Ds3231Alarm2Mode getAlarm2Mode() { return (Ds3231Alarm2Mode)alarme[1].modo; }
    void disableAlarm(uint8_t n)
    {
        if (n >= 1 && n <= 2) alarme[n - 1].ligado = false;
    }
    void clearAlarm(uint8_t n)
    {
        if (n < 1 || n > 2) return;
        conferir();
        alarme[n - 1].disparou = false;
    }
    bool alarmFired(uint8_t n)
    {
        if (n < 1 || n > 2) return false;
        conferir();
        return alarme[n - 1].disparou;
    }
    static uint8_t dowToDS3231(uint8_t d) { return d == 0 ? 7 : d; }

private:
    struct Alarme {
        DateTime quando;
        uint8_t modo = 0;
        bool ligado = false, disparou = false;
    } alarme[2];
    uint32_t visto = 0;
    bool saida32k = true;

    bool armar(int i, const DateTime &dt, uint8_t modo)
    {
        if (!dt.isValid()) return false;
        alarme[i].quando = dt;
        alarme[i].modo = modo;
        alarme[i].ligado = true;
        visto = now().unixtime();
        return true;
    }
    /* O chip compara a hora com o alarme a cada segundo (o 2, a cada minuto) e acende o flag. */
    bool bate(int i, const DateTime &t)
    {
        const DateTime &a = alarme[i].quando;
        uint8_t m = alarme[i].modo;
        if (i == 0) {
            if (m == DS3231_A1_PerSecond) return true;
            if (t.second() != a.second()) return false;
            if (m == DS3231_A1_Second) return true;
            if (t.minute() != a.minute()) return false;
            if (m == DS3231_A1_Minute) return true;
            if (t.hour() != a.hour()) return false;
            if (m == DS3231_A1_Hour) return true;
            return m == DS3231_A1_Day ? t.dayOfTheWeek() == a.dayOfTheWeek() : t.day() == a.day();
        }
        if (t.second() != 0) return false;
        if (m == DS3231_A2_PerMinute) return true;
        if (t.minute() != a.minute()) return false;
        if (m == DS3231_A2_Minute) return true;
        if (t.hour() != a.hour()) return false;
        if (m == DS3231_A2_Hour) return true;
        return m == DS3231_A2_Day ? t.dayOfTheWeek() == a.dayOfTheWeek() : t.day() == a.day();
    }
    void conferir()
    {
        uint32_t agora = now().unixtime();
        /* Os segundos que passaram desde a ultima olhada (um dia, no maximo: o resto ja teria batido). */
        uint32_t de = visto && agora > visto && agora - visto < 86400 ? visto + 1 : agora;
        for (uint32_t s = de; s <= agora; s++) {
            DateTime t(s);
            for (int i = 0; i < 2; i++)
                if (alarme[i].ligado && bate(i, t)) alarme[i].disparou = true;
        }
        visto = agora;
    }
};

/* O PCF8523 (o do Adalogger): a mesma hora do relogio do circuito, e o resto do chip guardado aqui. */
class RTC_PCF8523 : public RTC_DS1307 {
public:
    bool initialized(void) { return iniciado; }
    void adjust(const DateTime &dt)
    {
        RTC_DS1307::adjust(dt);
        iniciado = true;
    }
    void start(void) { parado = false; }
    void stop(void) { parado = true; }
    uint8_t isrunning() { return parado ? 0 : 1; }
    Pcf8523SqwPinMode readSqwPinMode() { return onda; }
    void writeSqwPinMode(Pcf8523SqwPinMode mode) { onda = mode; }
    void enableSecondTimer(void) {}
    void disableSecondTimer(void) {}
    void enableCountdownTimer(PCF8523TimerClockFreq, uint8_t, uint8_t) {}
    void enableCountdownTimer(PCF8523TimerClockFreq, uint8_t) {}
    void disableCountdownTimer(void) {}
    void deconfigureAllTimers(void) {}
    void calibrate(Pcf8523OffsetMode, int8_t) {}

private:
    bool iniciado = true, parado = false;
    Pcf8523SqwPinMode onda = PCF8523_OFF;
};

/* O PCF8563: outro chip, no endereco 0x51 -- sem ele no circuito, o begin() da falso. */
class RTC_PCF8563 : public RTC_DS1307 {
public:
    RTC_PCF8563() : RTC_DS1307("RTC_PCF8563") {}
    void start(void) { parado = false; }
    void stop(void) { parado = true; }
    uint8_t isrunning() { return parado ? 0 : 1; }
    Pcf8563SqwPinMode readSqwPinMode() { return onda; }
    void writeSqwPinMode(Pcf8563SqwPinMode mode) { onda = mode; }

private:
    bool parado = false;
    Pcf8563SqwPinMode onda = PCF8563_SquareWaveOFF;
};

/* Sem chip: o relogio do proprio micro (micros), com o ajuste de deriva, como na RTClib. */
class RTC_Micros {
public:
    void begin(const DateTime &dt) { adjust(dt); }
    void adjust(const DateTime &dt)
    {
        lastMicros = micros();
        lastUnix = dt.unixtime();
    }
    void adjustDrift(int ppm) { microsPerSecond = 1000000 + ppm; }
    DateTime now()
    {
        uint32_t elapsed = (micros() - lastMicros) / microsPerSecond;
        lastMicros += elapsed * microsPerSecond;
        lastUnix += elapsed;
        return DateTime(lastUnix);
    }

protected:
    uint32_t microsPerSecond = 1000000;
    uint32_t lastUnix = 0, lastMicros = 0;
};

/* Sem chip: o relogio do proprio micro (millis), como na RTClib. */
class RTC_Millis {
public:
    void begin(const DateTime &dt) { adjust(dt); }
    void adjust(const DateTime &dt)
    {
        lastMillis = millis();
        lastUnix = dt.unixtime();
    }
    DateTime now()
    {
        uint32_t elapsed = (millis() - lastMillis) / 1000;
        lastMillis += elapsed * 1000;
        lastUnix += elapsed;
        return DateTime(lastUnix);
    }

protected:
    uint32_t lastUnix = 0, lastMillis = 0;
};

#endif
