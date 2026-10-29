/* cs_i2s.cpp -- a saida I2S pelo simulador (ver cs_i2s.h). */
#include "cs_interno.h"
#include "cs_i2s.h"
#include "cs_objeto.h"

extern "C" bool cs_i2s_abrir(uint32_t taxa, int bits, int canais, int bclk, int ws, int dout)
{
    return ch_i(nullptr, "__i2s_abrir", cs_i(taxa), cs_i(bits), cs_i(canais), cs_i(bclk), cs_i(ws), cs_i(dout)) != 0;
}

/* Em pedacos de 8 KB: o simulador espera cada um tocar (o DMA). */
extern "C" size_t cs_i2s_escrever(const void *dados, size_t n)
{
    const uint8_t *p = (const uint8_t *)dados;
    size_t feito = 0;
    while (feito < n) {
        size_t pedaco = n - feito > 8192 ? 8192 : n - feito;
        cs_val a[] = {cs_de(cs_vetor(p + feito, (int)pedaco))};
        int64_t aceitos = cs_int(nullptr, "__i2s_escrever", 1, a);
        if (aceitos <= 0) break;
        feito += (size_t)aceitos;
    }
    return feito;
}

extern "C" void cs_i2s_fechar(void) { ch(nullptr, "__i2s_fechar"); }
