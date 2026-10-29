/* WString.h -- a String do Arduino.
 *
 * A mesma API do core (a do AVR com o que a ESP32 acrescentou: isEmpty,
 * a concatenacao de long long, o construtor de double), escrita de novo
 * aqui. O texto mora num buffer terminado em zero, como no original: o
 * c_str() aponta para ele e vale ate a String mudar. */
#ifndef String_class_h
#define String_class_h
#ifdef __cplusplus

#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <stdint.h>

class __FlashStringHelper;
#define F(string_literal) (reinterpret_cast<const __FlashStringHelper *>(string_literal))

class StringSumHelper;

class String {
    typedef void (String::*StringIfHelperType)() const;
    void StringIfHelper() const {}

public:
    String(const char *cstr = "");
    String(const char *cstr, unsigned int length);
    String(const uint8_t *cstr, unsigned int length) : String((const char *)cstr, length) {}
    String(const String &str);
    String(const __FlashStringHelper *str);
    String(String &&rval) noexcept;
    explicit String(char c);
    explicit String(unsigned char, unsigned char base = 10);
    explicit String(int, unsigned char base = 10);
    explicit String(unsigned int, unsigned char base = 10);
    explicit String(long, unsigned char base = 10);
    explicit String(unsigned long, unsigned char base = 10);
    explicit String(long long, unsigned char base = 10);
    explicit String(unsigned long long, unsigned char base = 10);
    explicit String(float, unsigned int decimalPlaces = 2);
    explicit String(double, unsigned int decimalPlaces = 2);
    ~String();

    bool reserve(unsigned int size);
    inline unsigned int length() const { return len; }
    inline bool isEmpty() const { return len == 0; }
    /* Esvazia a String (o 3.x da ESP32): o espaco reservado continua. */
    void clear()
    {
        if (buffer) buffer[0] = 0;
        len = 0;
    }

    String &operator=(const String &rhs);
    String &operator=(const char *cstr);
    String &operator=(const __FlashStringHelper *str);
    String &operator=(String &&rval) noexcept;

    bool concat(const String &str);
    bool concat(const char *cstr);
    bool concat(const char *cstr, unsigned int length);
    bool concat(const uint8_t *cstr, unsigned int length) { return concat((const char *)cstr, length); }
    bool concat(char c);
    bool concat(unsigned char num);
    bool concat(int num);
    bool concat(unsigned int num);
    bool concat(long num);
    bool concat(unsigned long num);
    bool concat(long long num);
    bool concat(unsigned long long num);
    bool concat(float num);
    bool concat(double num);
    bool concat(const __FlashStringHelper *str);

    template <class T> String &operator+=(const T &rhs) { concat(rhs); return *this; }
    String &operator+=(const char *cstr) { concat(cstr); return *this; }

    friend StringSumHelper &operator+(const StringSumHelper &lhs, const String &rhs);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, const char *cstr);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, char c);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, unsigned char num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, int num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, unsigned int num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, long num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, unsigned long num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, long long num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, unsigned long long num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, float num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, double num);
    friend StringSumHelper &operator+(const StringSumHelper &lhs, const __FlashStringHelper *rhs);

    operator StringIfHelperType() const { return buffer ? &String::StringIfHelper : 0; }

    int compareTo(const String &s) const;
    int compareTo(const char *cstr) const;
    bool equals(const String &s) const;
    bool equals(const char *cstr) const;
    bool operator==(const String &rhs) const { return equals(rhs); }
    bool operator==(const char *cstr) const { return equals(cstr); }
    bool operator!=(const String &rhs) const { return !equals(rhs); }
    bool operator!=(const char *cstr) const { return !equals(cstr); }
    bool operator<(const String &rhs) const { return compareTo(rhs) < 0; }
    bool operator>(const String &rhs) const { return compareTo(rhs) > 0; }
    bool operator<=(const String &rhs) const { return compareTo(rhs) <= 0; }
    bool operator>=(const String &rhs) const { return compareTo(rhs) >= 0; }
    bool equalsIgnoreCase(const String &s) const;
    bool equalsConstantTime(const String &s) const;
    bool startsWith(const String &prefix) const;
    bool startsWith(const String &prefix, unsigned int offset) const;
    bool endsWith(const String &suffix) const;

    char charAt(unsigned int index) const;
    void setCharAt(unsigned int index, char c);
    char operator[](unsigned int index) const;
    char &operator[](unsigned int index);
    void getBytes(unsigned char *buf, unsigned int bufsize, unsigned int index = 0) const;
    void toCharArray(char *buf, unsigned int bufsize, unsigned int index = 0) const
    {
        getBytes((unsigned char *)buf, bufsize, index);
    }
    const char *c_str() const { return buffer ? buffer : ""; }
    char *begin() { return buffer; }
    char *end() { return buffer + len; }
    const char *begin() const { return c_str(); }
    const char *end() const { return c_str() + len; }

    int indexOf(char ch) const;
    int indexOf(char ch, unsigned int fromIndex) const;
    int indexOf(const String &str) const;
    int indexOf(const String &str, unsigned int fromIndex) const;
    int lastIndexOf(char ch) const;
    int lastIndexOf(char ch, unsigned int fromIndex) const;
    int lastIndexOf(const String &str) const;
    int lastIndexOf(const String &str, unsigned int fromIndex) const;
    String substring(unsigned int beginIndex) const { return substring(beginIndex, len); }
    String substring(unsigned int beginIndex, unsigned int endIndex) const;

    void replace(char find, char replace);
    void replace(const String &find, const String &replace);
    void remove(unsigned int index);
    void remove(unsigned int index, unsigned int count);
    void toLowerCase(void);
    void toUpperCase(void);
    void trim(void);

    long toInt(void) const;
    float toFloat(void) const;
    double toDouble(void) const;

protected:
    char *buffer;
    unsigned int capacity;
    unsigned int len;

    void init(void);
    void invalidate(void);
    bool changeBuffer(unsigned int maxStrLen);
    String &copy(const char *cstr, unsigned int length);
    void move(String &rhs);
};

class StringSumHelper : public String {
public:
    StringSumHelper(const String &s) : String(s) {}
    StringSumHelper(const char *p) : String(p) {}
    StringSumHelper(char c) : String(c) {}
    StringSumHelper(unsigned char num) : String(num) {}
    StringSumHelper(int num) : String(num) {}
    StringSumHelper(unsigned int num) : String(num) {}
    StringSumHelper(long num) : String(num) {}
    StringSumHelper(unsigned long num) : String(num) {}
    StringSumHelper(long long num) : String(num) {}
    StringSumHelper(unsigned long long num) : String(num) {}
    StringSumHelper(float num) : String(num) {}
    StringSumHelper(double num) : String(num) {}
};

inline StringSumHelper operator+(const char *lhs, const String &rhs)
{
    StringSumHelper s(lhs);
    s.concat(rhs);
    return s;
}

#endif
#endif
