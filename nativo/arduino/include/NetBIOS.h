/* NetBIOS.h -- o nome NetBIOS da ESP32 (o Windows acha a placa por
 * "\\nome"): a placa responde as perguntas de nome na porta 137 (o NBNS),
 * pelo UDP da rede do simulador. A mesma API do core oficial. */
#ifndef __ESPNBNS_h__
#define __ESPNBNS_h__

#include "Arduino.h"
#include "AsyncUDP.h"
#include "WiFi.h"

class NetBIOS {
protected:
    AsyncUDP _udp;
    String _name;

    /* A pergunta "quem e NOME?" (0x20 = nome codificado, tipo NB) com o nosso
     * nome: responde com o IP. */
    void _onPacket(AsyncUDPPacket &packet)
    {
        if (packet.length() < 50) return;
        const uint8_t *d = packet.data();
        if ((d[2] & 0x80) || d[12] != 0x20) return;
        char nome[17] = {0};
        for (int i = 0; i < 16; i++) nome[i] = (char)(((d[13 + 2 * i] - 'A') << 4) | (d[14 + 2 * i] - 'A'));
        for (int i = 15; i >= 0 && (nome[i] == ' ' || nome[i] == 0); i--) nome[i] = 0;
        if (!_name.equalsIgnoreCase(nome)) return;
        uint8_t r[62];
        memcpy(r, d, 12);
        r[2] = 0x85;
        r[3] = 0x00;
        r[4] = r[5] = 0;
        r[6] = 0;
        r[7] = 1;
        r[8] = r[9] = r[10] = r[11] = 0;
        memcpy(r + 12, d + 12, 34);
        r[46] = 0x00, r[47] = 0x20, r[48] = 0x00, r[49] = 0x01;
        r[50] = 0, r[51] = 0, r[52] = 0, r[53] = 40;
        r[54] = 0, r[55] = 6, r[56] = 0, r[57] = 0;
        IPAddress ip = WiFi.localIP();
        for (int i = 0; i < 4; i++) r[58 + i] = ip[i];
        packet.write(r, sizeof(r));
    }

public:
    NetBIOS() {}
    ~NetBIOS() { end(); }
    bool begin(const char *name)
    {
        _name = name;
        _name.toUpperCase();
        if (!_udp.listen(137)) return false;
        _udp.onPacket([this](AsyncUDPPacket &p) { _onPacket(p); });
        return true;
    }
    void end() { _udp.close(); }
};

#if !defined(NO_GLOBAL_INSTANCES) && !defined(NO_GLOBAL_NETBIOS)
inline NetBIOS NBNS;
#endif

#endif
