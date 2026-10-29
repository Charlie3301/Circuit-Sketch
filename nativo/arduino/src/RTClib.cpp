/* RTClib.cpp -- as contas de data da RTClib (ver RTClib.h), com os mesmos
 * algoritmos: dias desde 2000, o dia da semana, os textos do __DATE__. */
#include "RTClib.h"

static const uint8_t diasNoMes[] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30};

static uint16_t date2days(uint16_t y, uint8_t m, uint8_t d)
{
    if (y >= 2000U) y -= 2000U;
    uint16_t days = d;
    for (uint8_t i = 1; i < m; ++i) days += diasNoMes[i - 1];
    if (m > 2 && y % 4 == 0) ++days;
    return days + 365 * y + (y + 3) / 4 - 1;
}

static uint32_t time2ulong(uint16_t days, uint8_t h, uint8_t m, uint8_t s)
{
    return ((days * 24UL + h) * 60 + m) * 60 + s;
}

static uint8_t conv2d(const char *p)
{
    uint8_t v = 0;
    if ('0' <= *p && *p <= '9') v = *p - '0';
    return 10 * v + *++p - '0';
}

DateTime::DateTime(uint32_t t)
{
    t -= SECONDS_FROM_1970_TO_2000;
    ss = t % 60;
    t /= 60;
    mm = t % 60;
    t /= 60;
    hh = t % 24;
    uint16_t days = t / 24;
    uint8_t leap;
    for (yOff = 0;; ++yOff) {
        leap = yOff % 4 == 0;
        if (days < 365U + leap) break;
        days -= 365 + leap;
    }
    for (m = 1; m < 12; ++m) {
        uint8_t daysPerMonth = diasNoMes[m - 1];
        if (leap && m == 2) ++daysPerMonth;
        if (days < daysPerMonth) break;
        days -= daysPerMonth;
    }
    d = days + 1;
}

DateTime::DateTime(uint16_t year, uint8_t month, uint8_t day, uint8_t hour, uint8_t min, uint8_t sec)
{
    if (year >= 2000U) year -= 2000U;
    yOff = (uint8_t)year;
    m = month;
    d = day;
    hh = hour;
    mm = min;
    ss = sec;
}

DateTime::DateTime(const char *date, const char *time)
{
    yOff = conv2d(date + 9);
    switch (date[0]) {
    case 'J': m = (date[1] == 'a') ? 1 : ((date[2] == 'n') ? 6 : 7); break;
    case 'F': m = 2; break;
    case 'A': m = date[2] == 'r' ? 4 : 8; break;
    case 'M': m = date[2] == 'r' ? 3 : 5; break;
    case 'S': m = 9; break;
    case 'O': m = 10; break;
    case 'N': m = 11; break;
    case 'D': m = 12; break;
    default: m = 1; break;
    }
    d = conv2d(date + 4);
    hh = conv2d(time);
    mm = conv2d(time + 3);
    ss = conv2d(time + 6);
}

DateTime::DateTime(const char *iso8601dateTime)
{
    char ref[] = "2000-01-01T00:00:00";
    memcpy(ref, iso8601dateTime, min(strlen(ref), strlen(iso8601dateTime)));
    yOff = conv2d(ref + 2);
    m = conv2d(ref + 5);
    d = conv2d(ref + 8);
    hh = conv2d(ref + 11);
    mm = conv2d(ref + 14);
    ss = conv2d(ref + 17);
}

bool DateTime::isValid() const
{
    if (yOff >= 100) return false;
    DateTime other(unixtime());
    return yOff == other.yOff && m == other.m && d == other.d && hh == other.hh && mm == other.mm && ss == other.ss;
}

char *DateTime::toString(char *buffer) const
{
    uint8_t apTag = (strstr(buffer, "ap") != nullptr) || (strstr(buffer, "AP") != nullptr);
    uint8_t hourReformatted = 0, isPM = 0;
    if (apTag) {
        if (hh == 0) {
            isPM = 0;
            hourReformatted = 12;
        } else if (hh == 12) {
            isPM = 1;
            hourReformatted = 12;
        } else if (hh < 12) {
            isPM = 0;
            hourReformatted = hh;
        } else {
            isPM = 1;
            hourReformatted = hh - 12;
        }
    }
    static const char *const meses = "JanFebMarAprMayJunJulAugSepOctNovDec";
    static const char *const dias = "SunMonTueWedThuFriSat";
    for (size_t i = 0; i < strlen(buffer) - 1; i++) {
        if (buffer[i] == 'h' && buffer[i + 1] == 'h') {
            if (!apTag) {
                buffer[i] = '0' + hh / 10;
                buffer[i + 1] = '0' + hh % 10;
            } else {
                buffer[i] = '0' + hourReformatted / 10;
                buffer[i + 1] = '0' + hourReformatted % 10;
            }
        }
        if (buffer[i] == 'm' && buffer[i + 1] == 'm') {
            buffer[i] = '0' + mm / 10;
            buffer[i + 1] = '0' + mm % 10;
        }
        if (buffer[i] == 's' && buffer[i + 1] == 's') {
            buffer[i] = '0' + ss / 10;
            buffer[i + 1] = '0' + ss % 10;
        }
        if (buffer[i] == 'D' && buffer[i + 1] == 'D' && buffer[i + 2] == 'D') {
            memcpy(buffer + i, dias + 3 * dayOfTheWeek(), 3);
        } else if (buffer[i] == 'D' && buffer[i + 1] == 'D') {
            buffer[i] = '0' + d / 10;
            buffer[i + 1] = '0' + d % 10;
        }
        if (buffer[i] == 'M' && buffer[i + 1] == 'M' && buffer[i + 2] == 'M') {
            memcpy(buffer + i, meses + 3 * (m - 1), 3);
        } else if (buffer[i] == 'M' && buffer[i + 1] == 'M') {
            buffer[i] = '0' + m / 10;
            buffer[i + 1] = '0' + m % 10;
        }
        if (buffer[i] == 'Y' && buffer[i + 1] == 'Y' && buffer[i + 2] == 'Y' && buffer[i + 3] == 'Y') {
            buffer[i] = '2';
            buffer[i + 1] = '0';
            buffer[i + 2] = '0' + (yOff / 10) % 10;
            buffer[i + 3] = '0' + yOff % 10;
        } else if (buffer[i] == 'Y' && buffer[i + 1] == 'Y') {
            buffer[i] = '0' + (yOff / 10) % 10;
            buffer[i + 1] = '0' + yOff % 10;
        }
        if (apTag && ((buffer[i] == 'a' || buffer[i] == 'A') && (buffer[i + 1] == 'p' || buffer[i + 1] == 'P'))) {
            buffer[i] = isPM ? (buffer[i] == 'a' ? 'p' : 'P') : buffer[i];
            buffer[i + 1] = buffer[i + 1] == 'p' ? 'm' : 'M';
        }
    }
    return buffer;
}

uint8_t DateTime::twelveHour() const
{
    if (hh == 0 || hh == 12) return 12;
    return hh > 12 ? hh - 12 : hh;
}

uint8_t DateTime::dayOfTheWeek() const
{
    uint16_t day = date2days(yOff, m, d);
    return (day + 6) % 7;
}

uint32_t DateTime::unixtime(void) const
{
    uint16_t days = date2days(yOff, m, d);
    return time2ulong(days, hh, mm, ss) + SECONDS_FROM_1970_TO_2000;
}

uint32_t DateTime::secondstime(void) const
{
    return time2ulong(date2days(yOff, m, d), hh, mm, ss);
}

String DateTime::timestamp(timestampOpt opt) const
{
    char buffer[25];
    switch (opt) {
    case TIMESTAMP_TIME: snprintf(buffer, sizeof(buffer), "%02d:%02d:%02d", hh, mm, ss); break;
    case TIMESTAMP_DATE: snprintf(buffer, sizeof(buffer), "%u-%02d-%02d", 2000U + yOff, m, d); break;
    default: snprintf(buffer, sizeof(buffer), "%u-%02d-%02dT%02d:%02d:%02d", 2000U + yOff, m, d, hh, mm, ss);
    }
    return String(buffer);
}

DateTime DateTime::operator+(const TimeSpan &span) const { return DateTime(unixtime() + span.totalseconds()); }
DateTime DateTime::operator-(const TimeSpan &span) const { return DateTime(unixtime() - span.totalseconds()); }
TimeSpan DateTime::operator-(const DateTime &right) const { return TimeSpan((int32_t)(unixtime() - right.unixtime())); }

void RTC_DS1307::adjust(const DateTime &dt)
{
    cs_val v[] = {cs_t("DateTime"), cs_i(dt.unixtime())};
    String data = cs_texto(nullptr, "__criar", 2, v);
    cs_obj_n(cs_nome, "adjust", cs_nome_de{data.c_str()});
    cs_obj_n(nullptr, "__destruir", data);
}

DateTime RTC_DS1307::now()
{
    String data = cs_obj_t(cs_nome, "now");
    DateTime agora((uint32_t)cs_obj_i(data.c_str(), "unixtime"));
    cs_obj_n(nullptr, "__destruir", data);
    return agora;
}
