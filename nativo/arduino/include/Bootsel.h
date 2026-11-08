/* Bootsel.h -- o botao BOOTSEL da Pico ("if (BOOTSEL)"), como no core
 * arduino-pico: le o botao da placa da bancada. */
#pragma once

class __Bootsel {
public:
    __Bootsel() {}
    operator bool();
};

extern __Bootsel BOOTSEL;
