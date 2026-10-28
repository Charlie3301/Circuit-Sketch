/* Print.h -- o print/println do Arduino, de todo lugar que escreve texto
 * (Serial, o LCD, o cliente de rede). Quem herda so precisa do write(). */
#ifndef Print_h
#define Print_h

#include <inttypes.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>

#include "WString.h"
#include "Printable.h"

#ifndef DEC
#define DEC 10
#endif

class Print {
private:
    int write_error;
    size_t printNumber(unsigned long long, uint8_t);
    size_t printFloat(double, uint8_t);

protected:
    void setWriteError(int err = 1) { write_error = err; }

public:
    Print() : write_error(0) {}
    virtual ~Print() {}

    int getWriteError() { return write_error; }
    void clearWriteError() { setWriteError(0); }

    virtual size_t write(uint8_t) = 0;
    size_t write(const char *str)
    {
        if (str == NULL) return 0;
        return write((const uint8_t *)str, strlen(str));
    }
    virtual size_t write(const uint8_t *buffer, size_t size);
    size_t write(const char *buffer, size_t size) { return write((const uint8_t *)buffer, size); }

    virtual int availableForWrite() { return 0; }
    virtual void flush() {}

    size_t printf(const char *format, ...) __attribute__((format(printf, 2, 3)));
    size_t vprintf(const char *format, va_list arg);

    size_t print(const __FlashStringHelper *);
    size_t print(const String &);
    size_t print(const char[]);
    size_t print(char);
    size_t print(unsigned char, int = DEC);
    size_t print(int, int = DEC);
    size_t print(unsigned int, int = DEC);
    size_t print(long, int = DEC);
    size_t print(unsigned long, int = DEC);
    size_t print(long long, int = DEC);
    size_t print(unsigned long long, int = DEC);
    size_t print(double, int = 2);
    size_t print(const Printable &);

    size_t println(const __FlashStringHelper *);
    size_t println(const String &s);
    size_t println(const char[]);
    size_t println(char);
    size_t println(unsigned char, int = DEC);
    size_t println(int, int = DEC);
    size_t println(unsigned int, int = DEC);
    size_t println(long, int = DEC);
    size_t println(unsigned long, int = DEC);
    size_t println(long long, int = DEC);
    size_t println(unsigned long long, int = DEC);
    size_t println(double, int = 2);
    size_t println(const Printable &);
    size_t println(void);
};

#endif
