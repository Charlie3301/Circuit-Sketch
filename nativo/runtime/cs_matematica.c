/* cs_matematica.c -- math.h.
 *
 * O que e so mexer nos bits do double (floor, fabs, frexp...) e feito
 * aqui. As funcoes transcendentes (seno, logaritmo, potencia) vem do
 * simulador, que as tem com a precisao do double. */
#include <stdint.h>
#include <math.h>
#include "cs_host.h"
#include "cs_interno.h"

typedef union { double d; uint64_t u; } bits_t;

double __cs_inf(void) { bits_t b; b.u = 0x7ff0000000000000ULL; return b.d; }
double __cs_nan(void) { bits_t b; b.u = 0x7ff8000000000000ULL; return b.d; }

int __cs_isnan(double x)
{
    bits_t b; b.d = x;
    return (b.u & 0x7fffffffffffffffULL) > 0x7ff0000000000000ULL;
}

int __cs_isinf(double x)
{
    bits_t b; b.d = x;
    return (b.u & 0x7fffffffffffffffULL) == 0x7ff0000000000000ULL;
}

int __cs_signbit(double x)
{
    bits_t b; b.d = x;
    return (int)(b.u >> 63);
}

static double m1(int qual, double a) { return cs->mat(qual, a, 0); }
static double m2(int qual, double a, double b) { return cs->mat(qual, a, b); }

double sin(double x) { return m1(CS_MAT_SIN, x); }
double cos(double x) { return m1(CS_MAT_COS, x); }
double tan(double x) { return m1(CS_MAT_TAN, x); }
double asin(double x) { return m1(CS_MAT_ASIN, x); }
double acos(double x) { return m1(CS_MAT_ACOS, x); }
double atan(double x) { return m1(CS_MAT_ATAN, x); }
double atan2(double y, double x) { return m2(CS_MAT_ATAN2, y, x); }
double sinh(double x) { return m1(CS_MAT_SINH, x); }
double cosh(double x) { return m1(CS_MAT_COSH, x); }
double tanh(double x) { return m1(CS_MAT_TANH, x); }
double asinh(double x) { return m1(CS_MAT_ASINH, x); }
double acosh(double x) { return m1(CS_MAT_ACOSH, x); }
double atanh(double x) { return m1(CS_MAT_ATANH, x); }
double exp(double x) { return m1(CS_MAT_EXP, x); }
double exp2(double x) { return m1(CS_MAT_EXP2, x); }
double expm1(double x) { return m1(CS_MAT_EXPM1, x); }
double log(double x) { return m1(CS_MAT_LOG, x); }
double log10(double x) { return m1(CS_MAT_LOG10, x); }
double log2(double x) { return m1(CS_MAT_LOG2, x); }
double log1p(double x) { return m1(CS_MAT_LOG1P, x); }
double pow(double x, double y) { return m2(CS_MAT_POW, x, y); }
double cbrt(double x) { return m1(CS_MAT_CBRT, x); }
double hypot(double x, double y) { return m2(CS_MAT_HYPOT, x, y); }
double fmod(double x, double y) { return m2(CS_MAT_FMOD, x, y); }
double remainder(double x, double y) { return m2(CS_MAT_REMAINDER, x, y); }
double erf(double x) { return m1(CS_MAT_ERF, x); }
double erfc(double x) { return m1(CS_MAT_ERFC, x); }
double tgamma(double x) { return m1(CS_MAT_TGAMMA, x); }
double lgamma(double x) { return m1(CS_MAT_LGAMMA, x); }

/* A raiz quadrada e a instrucao do processador: e rapida, exata e os
 * jogos a chamam muito. Vai em bytes (sqrtsd e sqrtss): o montador do
 * TinyCC nao as conhece. No x64 do Windows o argumento e a volta ficam
 * no xmm0. */
__asm__(
".text\n"
".global sqrt\n"
"sqrt:\n"
"  .byte 0xf2,0x0f,0x51,0xc0\n"
"  ret\n"
".global sqrtf\n"
"sqrtf:\n"
"  .byte 0xf3,0x0f,0x51,0xc0\n"
"  ret\n"
);

double fabs(double x)
{
    bits_t b; b.d = x;
    b.u &= 0x7fffffffffffffffULL;
    return b.d;
}

double copysign(double x, double y)
{
    bits_t a, b;
    a.d = x;
    b.d = y;
    a.u = (a.u & 0x7fffffffffffffffULL) | (b.u & 0x8000000000000000ULL);
    return a.d;
}

double trunc(double x)
{
    bits_t v; v.d = x;
    int e = (int)((v.u >> 52) & 0x7ff) - 1023;
    if (e >= 52) return x;
    if (e < 0) {
        v.u &= 0x8000000000000000ULL;
        return v.d;
    }
    v.u &= ~(0x000fffffffffffffULL >> e);
    return v.d;
}

double floor(double x)
{
    double t = trunc(x);
    return t > x ? t - 1.0 : t;
}

double ceil(double x)
{
    double t = trunc(x);
    return t < x ? t + 1.0 : t;
}

double round(double x)
{
    double t = trunc(x);
    double d = x - t;
    if (d >= 0.5) t += 1.0;
    else if (d <= -0.5) t -= 1.0;
    return t;
}

double rint(double x)
{
    double t = trunc(x);
    double d = x - t;
    if (d > 0.5 || (d == 0.5 && fmod(t, 2.0) != 0)) t += 1.0;
    else if (d < -0.5 || (d == -0.5 && fmod(t, 2.0) != 0)) t -= 1.0;
    return t;
}

double nearbyint(double x) { return rint(x); }
long lround(double x) { return (long)round(x); }
long long llround(double x) { return (long long)round(x); }
long lrint(double x) { return (long)rint(x); }
long long llrint(double x) { return (long long)rint(x); }

double modf(double x, double *inteiro)
{
    double t = trunc(x);
    *inteiro = t;
    if (__cs_isinf(x)) return copysign(0.0, x);
    return x - t;
}

double frexp(double x, int *expoente)
{
    bits_t v; v.d = x;
    int e = (int)((v.u >> 52) & 0x7ff);
    if (e == 0) {
        if ((v.u << 1) == 0) {
            *expoente = 0;
            return x;
        }
        /* subnormal: multiplica por 2^54 e desconta */
        v.d = x * 18014398509481984.0;
        e = (int)((v.u >> 52) & 0x7ff) - 54;
    } else if (e == 0x7ff) {
        *expoente = 0;
        return x;
    }
    *expoente = e - 1022;
    v.u = (v.u & 0x800fffffffffffffULL) | (1022ULL << 52);
    return v.d;
}

double scalbn(double x, int n)
{
    bits_t v;
    if (n > 1023) {
        x *= 8.98846567431158e307;
        n -= 1023;
        if (n > 1023) {
            x *= 8.98846567431158e307;
            n -= 1023;
            if (n > 1023) n = 1023;
        }
    } else if (n < -1022) {
        x *= 2.2250738585072014e-308 * 9007199254740992.0;
        n += 1022 - 53;
        if (n < -1022) {
            x *= 2.2250738585072014e-308 * 9007199254740992.0;
            n += 1022 - 53;
            if (n < -1022) n = -1022;
        }
    }
    v.u = (uint64_t)(0x3ff + n) << 52;
    return x * v.d;
}

double ldexp(double x, int n) { return scalbn(x, n); }

int ilogb(double x)
{
    int e;
    if (x == 0) return -2147483647 - 1;
    frexp(x, &e);
    return e - 1;
}

double logb(double x) { return (double)ilogb(x); }
double fmin(double x, double y) { return __cs_isnan(x) ? y : __cs_isnan(y) ? x : (x < y ? x : y); }
double fmax(double x, double y) { return __cs_isnan(x) ? y : __cs_isnan(y) ? x : (x > y ? x : y); }
double fdim(double x, double y) { return x > y ? x - y : 0.0; }
double fma(double x, double y, double z) { return x * y + z; }

double nextafter(double x, double y)
{
    bits_t v;
    if (__cs_isnan(x) || __cs_isnan(y)) return x + y;
    if (x == y) return y;
    if (x == 0) {
        v.u = 1;
        return y > 0 ? v.d : -v.d;
    }
    v.d = x;
    if ((x < y) == (x > 0)) v.u++;
    else v.u--;
    return v.d;
}

/* ---- as de float chamam as de double: o resultado arredondado para
 * float e o mesmo (a menos do ultimo bit, que a newlib tambem nao garante). */
float sinf(float x) { return (float)sin(x); }
float cosf(float x) { return (float)cos(x); }
float tanf(float x) { return (float)tan(x); }
float asinf(float x) { return (float)asin(x); }
float acosf(float x) { return (float)acos(x); }
float atanf(float x) { return (float)atan(x); }
float atan2f(float y, float x) { return (float)atan2(y, x); }
float sinhf(float x) { return (float)sinh(x); }
float coshf(float x) { return (float)cosh(x); }
float tanhf(float x) { return (float)tanh(x); }
float expf(float x) { return (float)exp(x); }
float exp2f(float x) { return (float)exp2(x); }
float logf(float x) { return (float)log(x); }
float log10f(float x) { return (float)log10(x); }
float log2f(float x) { return (float)log2(x); }
float powf(float x, float y) { return (float)pow(x, y); }
float cbrtf(float x) { return (float)cbrt(x); }
float ceilf(float x) { return (float)ceil(x); }
float floorf(float x) { return (float)floor(x); }
float roundf(float x) { return (float)round(x); }
float truncf(float x) { return (float)trunc(x); }
float rintf(float x) { return (float)rint(x); }
float fabsf(float x) { return (float)fabs(x); }
float fmodf(float x, float y) { return (float)fmod(x, y); }
float hypotf(float x, float y) { return (float)hypot(x, y); }
float fminf(float x, float y) { return (float)fmin(x, y); }
float fmaxf(float x, float y) { return (float)fmax(x, y); }
float copysignf(float x, float y) { return (float)copysign(x, y); }
float ldexpf(float x, int n) { return (float)ldexp(x, n); }
float frexpf(float x, int *e) { return (float)frexp(x, e); }
float modff(float x, float *i)
{
    double d;
    float r = (float)modf(x, &d);
    *i = (float)d;
    return r;
}
long lroundf(float x) { return lround(x); }
long lrintf(float x) { return lrint(x); }
