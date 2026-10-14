/* dap_fontes.h -- as letras da tela do PlayerDAP (Noto Sans, 4 bits por pixel). */
#ifndef DAP_FONTES_H
#define DAP_FONTES_H

#include <stdint.h>

typedef struct {
    uint16_t codigo;     /* o caractere (Unicode) */
    uint8_t larg, alt;   /* o desenho recortado */
    int8_t dx, dy;       /* onde ele comeca: dx a partir da caneta, dy a partir da linha de base (negativo = acima) */
    uint8_t avanco;      /* quanto a caneta anda */
    uint32_t pos;        /* onde o desenho comeca em dados (dois pixels por byte) */
} dap_glifo_t;

typedef struct {
    const dap_glifo_t *glifos;   /* em ordem de codigo */
    uint16_t quantos;
    const uint8_t *dados;
    uint8_t altura;              /* de uma linha a outra */
    uint8_t ascendente;          /* do topo da linha a linha de base */
} dap_fonte_t;

extern const dap_fonte_t dap_fonte_pequena;   /* 11 px */
extern const dap_fonte_t dap_fonte_normal;    /* 14 px */
extern const dap_fonte_t dap_fonte_forte;     /* 14 px, negrito */
extern const dap_fonte_t dap_fonte_titulo;    /* 18 px, negrito */
extern const dap_fonte_t dap_fonte_grande;    /* 28 px, negrito */
extern const dap_fonte_t dap_fonte_icones;          /* Material Icons, 16 px */
extern const dap_fonte_t dap_fonte_icones_medios;   /* 24 px */
extern const dap_fonte_t dap_fonte_icones_grandes;  /* 48 px */

#endif
