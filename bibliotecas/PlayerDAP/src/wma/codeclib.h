/* codeclib.h -- PlayerDAP: o pedaco do codeclib do Rockbox
 * (lib/rbcodec/codecs/lib/codeclib.h, GPL 2 ou mais nova) que o libwma usa,
 * no lugar do original, que puxa a API de codecs do Rockbox inteira. Os
 * atributos de memoria interna (IRAM) do Rockbox ficam vazios; o
 * bs_generic e o av_log2 sao os de la, na versao sem CLZ.
 */
#ifndef __CODECLIB_H__
#define __CODECLIB_H__

#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdlib.h>
#include "mdct.h"
#include "fft.h"

#define ROCKBOX_LITTLE_ENDIAN 1
#define ICODE_ATTR
#define ICONST_ATTR
#define IBSS_ATTR
#define IDATA_ATTR
#define MEM_ALIGN_ATTR
#define CACHEALIGN_ATTR
#define ICODE_ATTR_TREMOR_MDCT
#define ICODE_ATTR_WMA
#define LIKELY(x) (x)
#define UNLIKELY(x) (x)
#define DEBUGF(...) do { } while (0)
#define LOGF(...) do { } while (0)
#ifndef MIN
#define MIN(a, b) (((a) < (b)) ? (a) : (b))
#endif
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif

/*ffmpeg fft-based mdct */
extern void ff_imdct_half(unsigned int nbits, int32_t *output, const int32_t *input);
extern void ff_imdct_calc(unsigned int nbits, int32_t *output, const int32_t *input);
extern void ff_fft_calc_c(int nbits, FFTComplex *z);

extern const uint8_t bs_log2_tab[256];
extern const uint8_t bs_clz_tab[256];

#define BS_LOG2  0 /* default personality, equivalent floor(log2(x)) */
#define BS_CLZ   1 /* alternate personality, Count Leading Zeros */
#define BS_SHORT 2 /* input guaranteed not to exceed 16 bits */
#define BS_0_0   4 /* guarantee mapping of 0 input to 0 output */

static inline unsigned int bs_generic(unsigned int v, int mode)
{
    const uint8_t *bs_tab;
    unsigned int r;
    unsigned int n = v;
    int inc;
    if (mode & BS_CLZ)
    {
        bs_tab = bs_clz_tab;
        r = 24;
        inc = -16;
    } else {
        bs_tab = bs_log2_tab;
        r = 0;
        inc = 16;
    }
    if (!(mode & BS_SHORT) && n >= 0x10000) {
        n >>= 16;
        r += inc;
    }
    if (n > 0xff) {
        n >>= 8;
        r += inc / 2;
    }
    n = bs_tab[n];
    r += n;
    if (mode & BS_CLZ && mode & BS_0_0 && v == 0)
        r = 0;
    return r;
}

#define av_log2(v) bs_generic(v, BS_0_0)

/* o swap16/swap32 do platform.h do Rockbox */
static inline uint16_t swap16(uint16_t v) { return (uint16_t)(v << 8 | v >> 8); }
static inline uint32_t swap32(uint32_t v)
{
    return (v >> 24) | ((v >> 8) & 0xFF00) | ((v << 8) & 0xFF0000) | (v << 24);
}

#endif /* __CODECLIB_H__ */
