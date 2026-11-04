/* cs_idf_relogio.cpp -- o relogio do sistema que o programa acerta (settimeofday): daqui
 * em diante o gettimeofday, o time e o getLocalTime contam a partir da hora pedida. So entra
 * na ligacao de quem usa: o .a do core nao traz este arquivo a toa. */
#include "Arduino.h"
#if defined(ESP32)
#include <sys/time.h>
#include <time.h>

/* Sem o windows.h: ele briga com o INPUT e o OUTPUT do Arduino.h. */
extern "C" __declspec(dllimport) void __stdcall GetSystemTimeAsFileTime(unsigned long long *agora);

static int64_t ajusteEmUs;
static bool ajustado;

static int64_t agoraEmUs()
{
    unsigned long long t;
    GetSystemTimeAsFileTime(&t);
    return (int64_t)(t / 10) - 11644473600000000LL + ajusteEmUs; /* de 1601 para 1970 */
}

extern "C" int settimeofday(const struct timeval *tv, const struct timezone *)
{
    if (!tv) return 0;
    int64_t pedido = (int64_t)tv->tv_sec * 1000000 + tv->tv_usec;
    ajusteEmUs = 0;
    ajusteEmUs = pedido - agoraEmUs();
    ajustado = true;
    /* Sem TZ, a placa conta em UTC: o localtime nao pode usar o fuso do computador. */
    _putenv_s("TZ", "UTC0");
    _tzset();
    return 0;
}

/* O time() da libc do Windows e um inline que chama este. */
extern "C" __time64_t _time64(__time64_t *saida)
{
    __time64_t t = agoraEmUs() / 1000000;
    if (saida) *saida = t;
    return t;
}

extern "C" int gettimeofday(struct timeval *tv, void *)
{
    if (!tv) return 0;
    int64_t t = agoraEmUs();
    tv->tv_sec = (long)(t / 1000000);
    tv->tv_usec = (long)(t % 1000000);
    return 0;
}

extern "C" int cs_relogio_foi_ajustado(void) { return ajustado; }
#endif
