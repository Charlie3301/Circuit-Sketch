/* cs_bancada.c -- quanto este computador corre com o C do TinyCC.
 *
 * A CPU simulada precisa saber quantas vezes o computador e mais rapido que
 * o chip para o codigo nativo (o DOOM) esperar a diferenca. O chip tem a
 * medida publicada (o CoreMark de cada ESP32); o computador, esta bancada:
 * quatro contas no espirito do que um programa de placa faz -- andar numa
 * lista ligada, multiplicar matriz de 16 bits, uma maquina de estados sobre
 * texto e um CRC. No desenvolvimento ela foi medida junto com o CoreMark
 * compilado pelo mesmo TinyCC, e a razao entre as duas vale em qualquer
 * computador: sao o mesmo tipo de codigo, do mesmo compilador. */
#include <stdint.h>
#include <string.h>

typedef struct no {
    int16_t valor;
    uint16_t ordem;
    struct no *prox;
} no;

static uint16_t crc16(uint16_t crc, uint8_t dado)
{
    crc ^= dado;
    for (int i = 0; i < 8; i++) crc = (crc & 1) ? (uint16_t)((crc >> 1) ^ 0xA001) : (uint16_t)(crc >> 1);
    return crc;
}

static no nos[96];
static int16_t ma[12 * 12], mb[12 * 12];
static int32_t mc[12 * 12];
static char texto[160];

static int estados(const char *s)
{
    /* O classico da aula: numero inteiro, real, ou outra coisa. */
    int inteiros = 0, reais = 0, outros = 0, estado = 0;
    for (; *s; s++) {
        char c = *s;
        if (c == ',') {
            if (estado == 1) inteiros++;
            else if (estado == 2) reais++;
            else if (estado == 3) outros++;
            estado = 0;
        } else if (c >= '0' && c <= '9') {
            if (estado == 0) estado = 1;
        } else if (c == '.') {
            estado = estado == 1 ? 2 : 3;
        } else {
            estado = 3;
        }
    }
    return inteiros * 3 + reais * 5 + outros * 7;
}

uint32_t cs_bancada(int voltas)
{
    uint32_t soma = 0;
    uint16_t crc = 0;
    static const char pedacos[] = "12,3.14,abc,7,0.5,x9,42,,1.5e3,88,";
    size_t tam = strlen(pedacos);
    for (size_t i = 0; i + 1 < sizeof(texto); i++) texto[i] = pedacos[i % tam];
    texto[sizeof(texto) - 1] = 0;

    for (int v = 0; v < voltas; v++) {
        /* lista ligada: monta, acha o maior e inverte */
        for (int i = 0; i < 96; i++) {
            nos[i].valor = (int16_t)((i * 37 + v) & 0x3FF);
            nos[i].ordem = (uint16_t)i;
            nos[i].prox = i + 1 < 96 ? &nos[i + 1] : 0;
        }
        no *cabeca = &nos[0], *invertida = 0;
        int16_t maior = -32768;
        for (no *p = cabeca; p; p = p->prox)
            if (p->valor > maior) maior = p->valor;
        while (cabeca) {
            no *seguinte = cabeca->prox;
            cabeca->prox = invertida;
            invertida = cabeca;
            cabeca = seguinte;
        }
        soma += (uint32_t)maior + invertida->ordem;

        /* matriz 12x12 de 16 bits */
        for (int i = 0; i < 144; i++) {
            ma[i] = (int16_t)(i * 3 - v);
            mb[i] = (int16_t)(i ^ v);
        }
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++) {
                int32_t acc = 0;
                for (int k = 0; k < 12; k++) acc += (int32_t)ma[i * 12 + k] * mb[k * 12 + j];
                mc[i * 12 + j] = acc;
            }
        soma += (uint32_t)mc[(v * 7) % 144];

        /* maquina de estados sobre o texto */
        texto[v % 150] = (char)('0' + (v % 10));
        soma += (uint32_t)estados(texto);

        /* CRC do que saiu */
        crc = crc16(crc, (uint8_t)soma);
        crc = crc16(crc, (uint8_t)(soma >> 8));
    }
    return soma ^ crc;
}
