/* cs_objeto.cpp -- as conversoes de argumento que dependem do core (ver
 * cs_objeto.h): o &Wire e o &Serial viram o nome do objeto no simulador, e a
 * funcao de callback vira um nome que o simulador chama de volta. */
#include "cs_objeto.h"
#include "Wire.h"
#include "SPI.h"

cs_val cs_de(TwoWire *w) { return cs_nome(w ? w->nomeNoSimulador() : "Wire"); }
cs_val cs_de(TwoWire &w) { return cs_nome(w.nomeNoSimulador()); }
cs_val cs_de(SPIClass *s) { return cs_nome(s ? s->nomeNoSimulador() : "SPI"); }
cs_val cs_de(SPIClass &s) { return cs_nome(s.nomeNoSimulador()); }
cs_val cs_de(HardwareSerial *s) { return cs_nome(s ? s->nomeNoSimulador() : "Serial"); }
cs_val cs_de(HardwareSerial &s) { return cs_nome(s.nomeNoSimulador()); }

cs_val cs_de(Stream *s)
{
    if (auto *h = dynamic_cast<HardwareSerial *>(s)) return cs_nome(h->nomeNoSimulador());
    return cs_nome("Serial");
}

static thread_local char nomesDeRotina[8][24];
static thread_local int proximoNome;

cs_val cs_nome_da_rotina(int indice)
{
    char *nome = nomesDeRotina[proximoNome++ & 7];
    snprintf(nome, 24, "__nativa_%d", indice);
    return cs_nome(nome);
}
