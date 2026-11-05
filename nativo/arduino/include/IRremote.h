/* IRremote.h -- o receptor de infravermelho com o controle do circuito.
 *
 * As duas APIs da IRremote: a nova (IrReceiver.begin, IrReceiver.decode(),
 * IrReceiver.decodedIRData.command) e a antiga (IRrecv irrecv(pino);
 * irrecv.decode(&results); results.value). O codigo do botao e o do
 * simulador, no formato de cada uma. */
#ifndef _IR_REMOTE_H
#define _IR_REMOTE_H

#include "cs_objeto.h"

#define VERSION_IRREMOTE "4.5.0"
#define ENABLE_LED_FEEDBACK true
#define DISABLE_LED_FEEDBACK false
#define USE_DEFAULT_FEEDBACK_LED_PIN 0
#define IR_RECEIVE_PIN 2
#define REPEAT 0xFFFFFFFF

typedef enum { UNKNOWN = 0, PULSE_WIDTH, PULSE_DISTANCE, APPLE, DENON, JVC, LG, LG2, NEC, NEC2, ONKYO, PANASONIC,
               KASEIKYO, KASEIKYO_DENON, KASEIKYO_SHARP, KASEIKYO_JVC, KASEIKYO_MITSUBISHI, RC5, RC6, SAMSUNG,
               SAMSUNGLG, SAMSUNG48, SHARP, SONY } decode_type_t;

#define IRDATA_FLAGS_IS_REPEAT 0x01

struct IRData {
    decode_type_t protocol;
    uint16_t address;
    uint16_t command;
    uint16_t extra;
    uint16_t numberOfBits;
    uint8_t flags;
    uint32_t decodedRawData;
};

/* A API antiga: o resultado do decode(&results). */
class decode_results : public CsObjeto {
public:
    decode_results() { cs_criar("decode_results"); }
    decode_type_t decode_type = UNKNOWN;
    uint16_t address = 0;
    uint32_t value = 0;
    uint8_t bits = 0;
    bool overflow = false;
    const char *nomeNoSim() const { return cs_nome; }
};

class IRrecv : public CsObjeto {
    bool global = false;

    uint32_t campo(const char *alvo, const char *nome)
    {
        cs_val a[] = {cs_t(alvo), cs_t(nome)};
        return (uint32_t)cs_int(nullptr, "__ler", 2, a);
    }

public:
    IRData decodedIRData = {};

    IRrecv() : global(true) { snprintf(cs_nome, sizeof(cs_nome), "IrReceiver"); }
    IRrecv(uint8_t pin) { cs_criar("IRrecv", pin); }
    /* O segundo e o LED de retorno (IRremote) ou o tamanho do buffer (IRremoteESP8266). */
    IRrecv(uint8_t pin, unsigned int segundo, unsigned int timeout = 15, bool save_buffer = false)
    {
        (void)segundo;
        (void)timeout;
        (void)save_buffer;
        cs_criar("IRrecv", pin);
    }

    void begin(uint8_t pin, bool enableLEDFeedback = false, uint8_t feedbackLEDPin = 0)
    {
        (void)enableLEDFeedback;
        (void)feedbackLEDPin;
        cs_obj_n(cs_nome, "begin", pin);
    }
    void enableIRIn() { cs_obj_n(cs_nome, "enableIRIn"); }
    void start() { cs_obj_n(cs_nome, "start"); }
    void stop() { cs_obj_n(cs_nome, "stop"); }
    void end() { cs_obj_n(cs_nome, "end"); }
    void resume() { cs_obj_n(cs_nome, "resume"); }
    bool isIdle() { return cs_obj_i(cs_nome, "isIdle") != 0; }

    /* A nova: os campos vao para o decodedIRData. */
    bool decode()
    {
        if (cs_obj_i(cs_nome, "decode") == 0) return false;
        decodedIRData.decodedRawData = campo(cs_nome, "decodedIRData.decodedRawData");
        decodedIRData.command = (uint16_t)campo(cs_nome, "decodedIRData.command");
        decodedIRData.address = (uint16_t)campo(cs_nome, "decodedIRData.address");
        decodedIRData.protocol = (decode_type_t)campo(cs_nome, "decodedIRData.protocol");
        decodedIRData.numberOfBits = (uint16_t)campo(cs_nome, "decodedIRData.numberOfBits");
        decodedIRData.flags = (uint8_t)campo(cs_nome, "decodedIRData.flags");
        return true;
    }

    /* A antiga: o resultado vai para o decode_results. */
    bool decode(decode_results *results)
    {
        if (cs_obj_i(cs_nome, "decode", cs_nome_de{results->nomeNoSim()}) == 0) return false;
        results->value = campo(results->nomeNoSim(), "value");
        results->bits = (uint8_t)campo(results->nomeNoSim(), "bits");
        results->decode_type = (decode_type_t)campo(results->nomeNoSim(), "decode_type");
        return true;
    }

    const char *getProtocolString() { return "NEC"; }
    void printIRResultShort(Print *p) { (void)p; }
    void printIRSendUsage(Print *p) { (void)p; }
};

/* Uma por programa (a IRremote.hpp define a global no proprio cabecalho). */
inline IRrecv IrReceiver;

#endif
