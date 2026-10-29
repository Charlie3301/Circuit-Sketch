/* soc/gpio_struct.h -- o GPIO da ESP32 como struct (GPIO.out_w1ts = mascara,
 * GPIO.in, GPIO.out1.val): cada campo e o registrador (ver cs_registradores.h). */
#ifndef CS_GPIO_STRUCT_H
#define CS_GPIO_STRUCT_H

#include "cs_registradores.h"

#ifdef __cplusplus
struct cs_registro {
    uint32_t endereco;
    cs_registro &operator=(uint32_t v)
    {
        cs_reg_escrever(endereco, v);
        return *this;
    }
    operator uint32_t() const { return cs_reg_ler(endereco); }
    cs_registro &operator|=(uint32_t v) { return *this = (uint32_t)*this | v; }
    cs_registro &operator&=(uint32_t v) { return *this = (uint32_t)*this & v; }
    cs_registro &operator^=(uint32_t v) { return *this = (uint32_t)*this ^ v; }
};
struct cs_registro_alto {
    cs_registro val, data;
};
struct cs_gpio_dev {
    cs_registro out{GPIO_OUT_REG}, out_w1ts{GPIO_OUT_W1TS_REG}, out_w1tc{GPIO_OUT_W1TC_REG};
    cs_registro_alto out1{{GPIO_OUT1_REG}, {GPIO_OUT1_REG}}, out1_w1ts{{GPIO_OUT1_W1TS_REG}, {GPIO_OUT1_W1TS_REG}},
        out1_w1tc{{GPIO_OUT1_W1TC_REG}, {GPIO_OUT1_W1TC_REG}};
    cs_registro enable{GPIO_ENABLE_REG}, enable_w1ts{GPIO_ENABLE_W1TS_REG}, enable_w1tc{GPIO_ENABLE_W1TC_REG};
    cs_registro_alto enable1{{GPIO_ENABLE1_REG}, {GPIO_ENABLE1_REG}}, enable1_w1ts{{GPIO_ENABLE1_W1TS_REG}, {GPIO_ENABLE1_W1TS_REG}},
        enable1_w1tc{{GPIO_ENABLE1_W1TC_REG}, {GPIO_ENABLE1_W1TC_REG}};
    cs_registro in{GPIO_IN_REG};
    cs_registro_alto in1{{GPIO_IN1_REG}, {GPIO_IN1_REG}};
};
static cs_gpio_dev GPIO;
#endif

#endif
