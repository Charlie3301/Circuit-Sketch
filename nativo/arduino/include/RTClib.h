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

/* O relogio do circuito: o DS1307 do simulador (o DS3231 usa o mesmo). */
class RTC_DS1307 : public CsObjeto {
public:
    RTC_DS1307() { cs_criar("RTC_DS1307"); }
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
    void disable32K() {}
    void enable32K() {}
    bool isEnabled32K() { return false; }
    void clearAlarm(uint8_t) {}
    void disableAlarm(uint8_t) {}
    bool alarmFired(uint8_t) { return false; }
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
