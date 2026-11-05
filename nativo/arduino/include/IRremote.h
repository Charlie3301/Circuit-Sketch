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

#define IRDATA_FLAGS_EMPTY 0x00
#define IRDATA_FLAGS_IS_REPEAT 0x01
#define IRDATA_FLAGS_IS_AUTO_REPEAT 0x02
#define IRDATA_FLAGS_PARITY_FAILED 0x04
#define IRDATA_FLAGS_TOGGLE_BIT 0x08
#define IRDATA_FLAGS_EXTRA_INFO 0x10
#define IRDATA_FLAGS_IS_PROTOCOL_WITH_DIFFERENT_REPEAT 0x20
#define IRDATA_FLAGS_WAS_OVERFLOW 0x40
#define IRDATA_FLAGS_IS_MSB_FIRST 0x80
#define IRDATA_FLAGS_IS_LSB_FIRST 0x00
#define NO_REPEATS 0
#define SEND_REPEAT_COMMAND true
#define IR_SEND_PIN 3
#define RECORD_GAP_MICROS 8000
#define MICROS_PER_TICK 50
#define RAW_BUFFER_LENGTH 200
#define MARK_EXCESS_MICROS 20
typedef uint32_t IRDecodedRawDataType;
typedef uint32_t IRRawDataType;
typedef uint8_t IRRawlenType;
typedef uint16_t IRRawbufType;
#define BITS_IN_RAW_DATA_TYPE 32
#define RAW_DATA_ARRAY_SIZE ((((RAW_BUFFER_LENGTH - 2) - 1) / (2 * BITS_IN_RAW_DATA_TYPE)) + 1)

/* O estado do receptor, como o da IRremote: o rawlen e o tamanho do quadro que chegou (o
 * espaco antes, o cabecalho, as marcas e espacos de cada bit e a marca final). */
struct irparams_struct {
    volatile uint8_t StateForISR;
    uint_fast8_t IRReceivePin;
    volatile uint_fast16_t TickCounterForISR;
    bool OverflowFlag;
    IRRawlenType rawlen;
    uint16_t initialGapTicks;
    IRRawbufType rawbuf[RAW_BUFFER_LENGTH];
};

struct IRData {
    decode_type_t protocol;
    uint16_t address;
    uint16_t command;
    uint16_t extra;
    uint16_t numberOfBits;
    uint8_t flags;
    IRDecodedRawDataType decodedRawData;
    IRRawDataType decodedRawDataArray[RAW_DATA_ARRAY_SIZE];
    irparams_struct *rawDataPtr;
};

/* Os nomes da IRremote, na ordem do decode_type_t. */
inline const char *getProtocolString(decode_type_t protocolo)
{
    static const char *const nomes[] = {"UNKNOWN", "PulseWidth", "PulseDistance", "Apple", "Denon", "JVC", "LG", "LG2", "NEC", "NEC2",
                                        "Onkyo", "Panasonic", "Kaseikyo", "Kaseikyo_Denon", "Kaseikyo_Sharp", "Kaseikyo_JVC",
                                        "Kaseikyo_Mitsubishi", "RC5", "RC6", "Samsung", "SamsungLG", "Samsung48", "Sharp", "Sony"};
    unsigned i = (unsigned)protocolo;
    return i < sizeof(nomes) / sizeof(nomes[0]) ? nomes[i] : "UNKNOWN";
}

/* Os protocolos que o receptor do simulador entende: o controle do circuito manda NEC. */
inline void printActiveIRProtocols(Print *aSerial)
{
    aSerial->print(F("NEC/NEC2/Onkyo/Apple, Panasonic/Kaseikyo, Denon/Sharp, Sony, RC5, RC6, LG, JVC, Samsung, "));
}

inline void printIRResultShort(Print *aSerial, IRData *dados)
{
    if (dados->flags & IRDATA_FLAGS_WAS_OVERFLOW) {
        aSerial->println(F("Overflow"));
        return;
    }
    aSerial->print(F("Protocol="));
    aSerial->print(getProtocolString(dados->protocol));
    if (dados->protocol == UNKNOWN) {
        aSerial->println();
        return;
    }
    aSerial->print(F(" Address=0x"));
    aSerial->print(dados->address, HEX);
    aSerial->print(F(" Command=0x"));
    aSerial->print(dados->command, HEX);
    if (dados->flags & IRDATA_FLAGS_EXTRA_INFO) {
        aSerial->print(F(" Extra=0x"));
        aSerial->print(dados->extra, HEX);
    }
    if (dados->flags & IRDATA_FLAGS_PARITY_FAILED) aSerial->print(F(" Parity fail"));
    if (dados->flags & IRDATA_FLAGS_TOGGLE_BIT) aSerial->print(F(" Toggle=1"));
    if (!(dados->flags & IRDATA_FLAGS_IS_REPEAT) || dados->decodedRawData != 0) {
        aSerial->print(F(" Raw-Data=0x"));
        aSerial->print(dados->decodedRawData, HEX);
        aSerial->print(' ');
        aSerial->print(dados->numberOfBits, DEC);
        aSerial->print(F(" bits"));
        aSerial->print(dados->flags & IRDATA_FLAGS_IS_MSB_FIRST ? F(" MSB first") : F(" LSB first"));
    }
    if (dados->flags & (IRDATA_FLAGS_IS_REPEAT | IRDATA_FLAGS_IS_AUTO_REPEAT)) {
        aSerial->print(' ');
        if (dados->flags & IRDATA_FLAGS_IS_AUTO_REPEAT) aSerial->print(F("Auto-"));
        aSerial->print(F("Repeat"));
    }
    aSerial->println();
}

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
    irparams_struct irparams = {};

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
        decodedIRData.decodedRawDataArray[0] = decodedIRData.decodedRawData;
        decodedIRData.rawDataPtr = &irparams;
        irparams.rawlen = (IRRawlenType)(decodedIRData.numberOfBits ? 2 * decodedIRData.numberOfBits + 4 : 0);
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

    const char *getProtocolString() { return ::getProtocolString(decodedIRData.protocol); }
    void printIRResultShort(Print *p, bool = true) { ::printIRResultShort(p, &decodedIRData); }
    void printIRResultMinimal(Print *p)
    {
        p->print(F("P="));
        p->print(getProtocolString());
        p->print(F(" A=0x"));
        p->print(decodedIRData.address, HEX);
        p->print(F(" C=0x"));
        p->print(decodedIRData.command, HEX);
        p->print(F(" Raw=0x"));
        p->println(decodedIRData.decodedRawData, HEX);
    }
    /* Como reenviar o que chegou, no formato do IrSender. */
    void printIRSendUsage(Print *p)
    {
        if (decodedIRData.protocol == UNKNOWN) return;
        p->print(F("Send with: IrSender.send"));
        p->print(getProtocolString());
        p->print(F("(0x"));
        p->print(decodedIRData.address, HEX);
        p->print(F(", 0x"));
        p->print(decodedIRData.command, HEX);
        p->println(F(", <numberOfRepeats>);"));
    }
    void printIRResultRawFormatted(Print *p, bool = true) { p->println(F("rawData[0]: (o receptor do simulador entrega o codigo ja decodificado)")); }
    bool available() { return cs_obj_i(cs_nome, "isIdle") == 0; }
    IRData *read() { return decode() ? &decodedIRData : nullptr; }
    void restartAfterSend() { resume(); }
    /* O temporizador de 50 us da IRremote: o receptor do simulador nao tem um. */
    void stopTimer() {}
    void restartTimer() {}
    void restartTimer(uint32_t aMicrosecondsToAddToGapCounter) { (void)aMicrosecondsToAddToGapCounter; }
    void restartTimerWithTicksToAdd(uint16_t aTicksToAddToGapCounter) { (void)aTicksToAddToGapCounter; }
};

/* O emissor: o LED de infravermelho pisca no pino o envelope de cada protocolo (as marcas e os
 * espacos, sem a portadora de 38 kHz) -- o analisador logico e o LED do circuito veem o quadro. */
class IRsend {
public:
    IRsend() {}
    explicit IRsend(uint8_t pino) : pino(pino) {}
    void begin() { prepararPino(); }
    void begin(uint8_t sendPin)
    {
        pino = sendPin;
        prepararPino();
    }
    void begin(uint8_t sendPin, bool, uint8_t) { begin(sendPin); }
    void begin(bool, uint8_t) { prepararPino(); }
    void setSendPin(uint8_t sendPin) { pino = sendPin; }
    void enableIROut(uint_fast8_t khz) { (void)khz; }
    void mark(uint16_t us)
    {
        digitalWrite(pino, HIGH);
        delayMicroseconds(us);
        digitalWrite(pino, LOW);
    }
    void space(uint16_t us) { delayMicroseconds(us); }
    void sendRaw(const uint16_t buf[], uint_fast16_t len, uint_fast8_t khz)
    {
        (void)khz;
        for (uint_fast16_t i = 0; i < len; i++) {
            if (i & 1) space(buf[i]);
            else mark(buf[i]);
        }
    }
    void sendRaw(const uint8_t buf[], uint_fast16_t len, uint_fast8_t khz)
    {
        (void)khz;
        for (uint_fast16_t i = 0; i < len; i++) {
            if (i & 1) space((uint16_t)buf[i] * 50);
            else mark((uint16_t)buf[i] * 50);
        }
    }

    /* NEC e os da mesma familia: 9 ms de marca, 4,5 ms de espaco, 32 bits LSB primeiro. */
    void sendNEC(uint16_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS) { nec(address, command, repeats, true); }
    void sendNEC2(uint16_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS) { nec(address, command, repeats, false); }
    void sendOnkyo(uint16_t address, uint16_t command, int_fast8_t repeats = NO_REPEATS)
    {
        quadros(repeats, [&] { distancia(9000, 4500, 560, 560, 1690, (uint32_t)address | (uint32_t)command << 16, 32); });
    }
    void sendApple(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS)
    {
        quadros(repeats, [&] { distancia(9000, 4500, 560, 560, 1690, 0x87EEu | (uint32_t)command << 16 | (uint32_t)address << 24, 32); });
    }
    void sendSamsung(uint16_t address, uint16_t command, int_fast8_t repeats = NO_REPEATS)
    {
        uint32_t a = address <= 0xFF ? (uint32_t)address | (uint32_t)address << 8 : address;
        uint32_t c = command <= 0xFF ? (uint32_t)command | (uint32_t)(uint8_t)~command << 8 : command;
        quadros(repeats, [&] { distancia(4500, 4500, 560, 560, 1690, a | c << 16, 32); });
    }
    void sendSamsungLG(uint16_t address, uint16_t command, int_fast8_t repeats = NO_REPEATS) { sendSamsung(address, command, repeats); }
    void sendLG(uint8_t address, uint16_t command, int_fast8_t repeats = NO_REPEATS)
    {
        uint32_t v = (uint32_t)address << 20 | (uint32_t)command << 4;
        uint8_t soma = 0;
        for (int i = 4; i < 20; i += 4) soma += (v >> i) & 0xF;
        v |= soma & 0xF;
        quadros(repeats, [&] { distanciaMsb(8000, 4000, 600, 550, 1600, v, 28); });
    }
    void sendJVC(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS)
    {
        quadros(repeats, [&] { distancia(8400, 4200, 525, 525, 1575, (uint32_t)address | (uint32_t)command << 8, 16); });
    }
    void sendSony(uint16_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS, uint8_t bits = 12)
    {
        uint32_t v = (uint32_t)(command & 0x7F) | (uint32_t)address << 7;
        quadros(repeats, [&] {
            mark(2400);
            for (uint8_t i = 0; i < bits; i++) {
                space(600);
                mark(v >> i & 1 ? 1200 : 600);
            }
            space(25000);
        });
    }
    void sendPanasonic(uint16_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS) { kaseikyo(address, command, repeats, 0x2002); }
    void sendKaseikyo(uint16_t address, uint8_t command, int_fast8_t repeats, uint16_t vendor) { kaseikyo(address, command, repeats, vendor); }
    void sendDenon(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS, uint8_t = 0)
    {
        quadros(repeats, [&] { distancia(0, 0, 260, 780, 1820, (uint32_t)address | (uint32_t)command << 5, 15); });
    }
    void sendSharp(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS) { sendDenon(address, command, repeats, 1); }
    /* RC5 e RC6: codificacao Manchester, um meio-bit de 889 us (444 us no RC6). */
    void sendRC5(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS, bool toggle = true)
    {
        if (toggle) alterna5 = !alterna5;
        uint16_t v = (uint16_t)(1u << 13 | (command < 64 ? 1u << 12 : 0) | (alterna5 ? 1u << 11 : 0) | (address & 0x1F) << 6 | (command & 0x3F));
        quadros(repeats, [&] { manchester(v, 14, 889); });
    }
    void sendRC6(uint8_t address, uint8_t command, int_fast8_t repeats = NO_REPEATS, bool toggle = true)
    {
        if (toggle) alterna6 = !alterna6;
        quadros(repeats, [&] {
            mark(2666);
            space(889);
            manchester(0x8u | (alterna6 ? 1u : 0u), 4, 444);
            manchester((uint16_t)((uint16_t)address << 8 | command), 16, 444);
        });
    }
    size_t write(IRData *dados, int_fast8_t repeats = NO_REPEATS)
    {
        switch (dados->protocol) {
        case NEC: sendNEC(dados->address, (uint8_t)dados->command, repeats); break;
        case NEC2: sendNEC2(dados->address, (uint8_t)dados->command, repeats); break;
        case ONKYO: sendOnkyo(dados->address, dados->command, repeats); break;
        case APPLE: sendApple((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        case SAMSUNG: case SAMSUNGLG: sendSamsung(dados->address, dados->command, repeats); break;
        case LG: case LG2: sendLG((uint8_t)dados->address, dados->command, repeats); break;
        case JVC: sendJVC((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        case SONY: sendSony(dados->address, (uint8_t)dados->command, repeats, (uint8_t)dados->numberOfBits); break;
        case PANASONIC: sendPanasonic(dados->address, (uint8_t)dados->command, repeats); break;
        case DENON: sendDenon((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        case SHARP: sendSharp((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        case RC5: sendRC5((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        case RC6: sendRC6((uint8_t)dados->address, (uint8_t)dados->command, repeats); break;
        default: return 0;
        }
        return 1;
    }
    size_t write(decode_type_t protocolo, uint16_t address, uint16_t command, int_fast8_t repeats = NO_REPEATS)
    {
        IRData d = {};
        d.protocol = protocolo;
        d.address = address;
        d.command = command;
        return write(&d, repeats);
    }

private:
    uint8_t pino = IR_SEND_PIN;
    bool alterna5 = false, alterna6 = false;

    void prepararPino()
    {
        pinMode(pino, OUTPUT);
        digitalWrite(pino, LOW);
    }
    template <class F> void quadros(int_fast8_t repeats, F quadro)
    {
        for (int_fast8_t i = 0; i <= repeats; i++) {
            quadro();
            if (i < repeats) delay(40);
        }
    }
    void distancia(uint16_t marcaInicial, uint16_t espacoInicial, uint16_t marca, uint16_t zero, uint16_t um, uint32_t dados, uint8_t bits)
    {
        if (marcaInicial) {
            mark(marcaInicial);
            space(espacoInicial);
        }
        for (uint8_t i = 0; i < bits; i++) {
            mark(marca);
            space(dados >> i & 1 ? um : zero);
        }
        mark(marca);
    }
    void distanciaMsb(uint16_t marcaInicial, uint16_t espacoInicial, uint16_t marca, uint16_t zero, uint16_t um, uint32_t dados, uint8_t bits)
    {
        mark(marcaInicial);
        space(espacoInicial);
        for (int i = bits - 1; i >= 0; i--) {
            mark(marca);
            space(dados >> i & 1 ? um : zero);
        }
        mark(marca);
    }
    void nec(uint16_t address, uint8_t command, int_fast8_t repeats, bool repeticaoCurta)
    {
        uint32_t a = address <= 0xFF ? (uint32_t)address | (uint32_t)(uint8_t)~address << 8 : address;
        uint32_t v = a | (uint32_t)command << 16 | (uint32_t)(uint8_t)~command << 24;
        distancia(9000, 4500, 560, 560, 1690, v, 32);
        for (int_fast8_t i = 0; i < repeats; i++) {
            delay(40);
            if (repeticaoCurta) {
                mark(9000);
                space(2250);
                mark(560);
            } else {
                distancia(9000, 4500, 560, 560, 1690, v, 32);
            }
        }
    }
    void kaseikyo(uint16_t address, uint8_t command, int_fast8_t repeats, uint16_t vendor)
    {
        uint8_t paridadeDoFabricante = (uint8_t)(vendor ^ vendor >> 8);
        paridadeDoFabricante = (paridadeDoFabricante ^ paridadeDoFabricante >> 4) & 0xF;
        uint32_t v = (uint32_t)paridadeDoFabricante | (uint32_t)(address & 0xFFF) << 4 | (uint32_t)command << 16;
        uint8_t paridade = (uint8_t)(v >> 8 ^ v >> 16);
        quadros(repeats, [&] {
            mark(3456);
            space(1728);
            for (int i = 0; i < 16; i++) {
                mark(432);
                space(vendor >> i & 1 ? 1296 : 432);
            }
            for (int i = 0; i < 24; i++) {
                mark(432);
                space(v >> i & 1 ? 1296 : 432);
            }
            for (int i = 0; i < 8; i++) {
                mark(432);
                space(paridade >> i & 1 ? 1296 : 432);
            }
            mark(432);
        });
    }
    void manchester(uint16_t dados, uint8_t bits, uint16_t meio)
    {
        for (int i = bits - 1; i >= 0; i--) {
            if (dados >> i & 1) {
                space(meio);
                mark(meio);
            } else {
                mark(meio);
                space(meio);
            }
        }
    }
};

/* Um por programa, como o da IRremote.hpp. */
inline IRsend IrSender;

/* Uma por programa (a IRremote.hpp define a global no proprio cabecalho). */
inline IRrecv IrReceiver;

#endif
