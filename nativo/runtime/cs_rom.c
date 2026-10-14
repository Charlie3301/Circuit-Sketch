/* cs_rom.c -- funcoes que a ESP32 tem na ROM.
 *
 * Na placa elas ja estao gravadas no chip, e o linker do ESP-IDF da o
 * endereco delas para qualquer codigo (esp32s3.rom.ld: MD5Init = ...):
 * quem chama MD5Init nao precisa de biblioteca nenhuma. Aqui, o mesmo. */
#include <stdint.h>
#include <string.h>

/* O MD5 da ROM (RFC 1321), com o contexto do rom/md5_hash.h. */
typedef struct {
    uint32_t buf[4];
    uint32_t bits[2];
    uint8_t in[64];
} cs_md5;

static void md5_bloco(uint32_t estado[4], const uint8_t *p)
{
    static const uint32_t k[64] = {
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
    };
    static const uint8_t giro[16] = { 7, 12, 17, 22, 5, 9, 14, 20, 4, 11, 16, 23, 6, 10, 15, 21 };
    uint32_t w[16];
    for (int i = 0; i < 16; i++)
        w[i] = p[i * 4] | (uint32_t)p[i * 4 + 1] << 8 | (uint32_t)p[i * 4 + 2] << 16 | (uint32_t)p[i * 4 + 3] << 24;
    uint32_t a = estado[0], b = estado[1], c = estado[2], d = estado[3];
    for (int i = 0; i < 64; i++) {
        uint32_t f;
        int g;
        if (i < 16) { f = (b & c) | (~b & d); g = i; }
        else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) & 15; }
        else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) & 15; }
        else { f = c ^ (b | ~d); g = (7 * i) & 15; }
        uint32_t soma = a + f + k[i] + w[g];
        int s = giro[(i >> 4) * 4 + (i & 3)];
        a = d;
        d = c;
        c = b;
        b = b + ((soma << s) | (soma >> (32 - s)));
    }
    estado[0] += a;
    estado[1] += b;
    estado[2] += c;
    estado[3] += d;
}

void MD5Init(cs_md5 *c)
{
    c->buf[0] = 0x67452301;
    c->buf[1] = 0xefcdab89;
    c->buf[2] = 0x98badcfe;
    c->buf[3] = 0x10325476;
    c->bits[0] = c->bits[1] = 0;
}

void MD5Update(cs_md5 *c, const unsigned char *dados, unsigned n)
{
    uint32_t usado = (c->bits[0] >> 3) & 63;
    uint32_t antes = c->bits[0];
    c->bits[0] += (uint32_t)n << 3;
    if (c->bits[0] < antes) c->bits[1]++;
    c->bits[1] += (uint32_t)n >> 29;
    if (usado) {
        uint32_t falta = 64 - usado;
        if (n < falta) {
            memcpy(c->in + usado, dados, n);
            return;
        }
        memcpy(c->in + usado, dados, falta);
        md5_bloco(c->buf, c->in);
        dados += falta;
        n -= falta;
    }
    for (; n >= 64; dados += 64, n -= 64) md5_bloco(c->buf, dados);
    memcpy(c->in, dados, n);
}

void MD5Final(unsigned char resumo[16], cs_md5 *c)
{
    uint32_t usado = (c->bits[0] >> 3) & 63;
    c->in[usado++] = 0x80;
    if (usado > 56) {
        memset(c->in + usado, 0, 64 - usado);
        md5_bloco(c->buf, c->in);
        usado = 0;
    }
    memset(c->in + usado, 0, 56 - usado);
    for (int i = 0; i < 4; i++) {
        c->in[56 + i] = (uint8_t)(c->bits[0] >> (8 * i));
        c->in[60 + i] = (uint8_t)(c->bits[1] >> (8 * i));
    }
    md5_bloco(c->buf, c->in);
    for (int i = 0; i < 16; i++) resumo[i] = (uint8_t)(c->buf[i / 4] >> (8 * (i % 4)));
    memset(c, 0, sizeof(*c));
}

/* Os nomes do esp_rom_md5.h (no linker: PROVIDE(esp_rom_md5_init = MD5Init)). */
void esp_rom_md5_init(cs_md5 *c) { MD5Init(c); }
void esp_rom_md5_update(cs_md5 *c, const void *dados, uint32_t n) { MD5Update(c, dados, n); }
void esp_rom_md5_final(uint8_t *resumo, cs_md5 *c) { MD5Final(resumo, c); }
