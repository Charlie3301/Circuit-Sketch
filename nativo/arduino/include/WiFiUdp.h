/* WiFiUdp.h -- o UDP da ESP32 (WiFiUDP, que no core 3.x e o NetworkUDP) pela
 * rede do simulador: entre as placas da bancada (inclusive broadcast e
 * multicast), com o computador (a mesma porta no 127.0.0.1) e com a internet.
 * O NTP (porta 123) a rede responde com o relogio do computador.
 *
 * Como no core oficial: o pacote que chega cabe em 1460 bytes, o que vai sai
 * no endPacket(), e o beginPacket() sem begin() abre uma porta qualquer. */
#ifndef _WIFIUDP_H_
#define _WIFIUDP_H_

#include "Arduino.h"
#include "Udp.h"
#include "cs_objeto.h"
#include <vector>

class NetworkUDP : public UDP {
    int id = -1;
    uint16_t porta = 0;
    IPAddress grupo;
    std::vector<uint8_t> saida;
    String destino;
    uint16_t portaDestino = 0;
    bool montando = false;
    uint8_t entrada[1460];
    size_t tamanho = 0, posicao = 0;
    IPAddress ipRemoto;
    uint16_t portaRemota = 0;

    bool abrir(uint16_t p)
    {
        id = (int)cs_obj_i(nullptr, "__udp_abrir", (int)p);
        if (id < 0) return false;
        porta = (uint16_t)cs_obj_i(nullptr, "__udp_porta", id);
        return true;
    }

public:
    NetworkUDP() {}
    ~NetworkUDP() { stop(); }
    NetworkUDP(const NetworkUDP &) = delete;
    NetworkUDP &operator=(const NetworkUDP &) = delete;

    uint8_t begin(IPAddress a, uint16_t p) { (void)a; return begin(p); }
    uint8_t begin(uint16_t p) override
    {
        stop();
        return abrir(p) ? 1 : 0;
    }
    uint8_t beginMulticast(IPAddress a, uint16_t p) override
    {
        if (!begin(p)) return 0;
        grupo = a;
        cs_obj_n(nullptr, "__udp_grupo", id, a.toString());
        return 1;
    }
    void stop() override
    {
        if (id >= 0) cs_obj_n(nullptr, "__udp_fechar", id);
        id = -1;
        porta = 0;
        tamanho = posicao = 0;
        saida.clear();
        montando = false;
    }

    int beginMulticastPacket()
    {
        if (!porta || grupo.isAny()) return 0;
        return beginPacket(grupo, porta);
    }
    /* A resposta para quem mandou o ultimo pacote. */
    int beginPacket()
    {
        if (!portaRemota) return 0;
        return beginPacket(ipRemoto, portaRemota);
    }
    int beginPacket(IPAddress ip, uint16_t port) override { return beginPacket(ip.toString().c_str(), port); }
    int beginPacket(const char *host, uint16_t port) override
    {
        if (id < 0 && !abrir(0)) return 0;
        destino = host ? host : "";
        portaDestino = port;
        saida.clear();
        montando = true;
        return 1;
    }
    int endPacket() override
    {
        if (!montando || id < 0) return 0;
        montando = false;
        bool foi = cs_obj_i(nullptr, "__udp_enviar", id, destino, (int)portaDestino, cs_vetor(saida.data(), (int)saida.size())) != 0;
        saida.clear();
        return foi ? 1 : 0;
    }
    size_t write(uint8_t c) override { return write(&c, 1); }
    size_t write(const uint8_t *buffer, size_t size) override
    {
        if (!montando) return 0;
        /* O core junta no maximo 1460 bytes por pacote. */
        size_t cabe = saida.size() >= 1460 ? 0 : 1460 - saida.size();
        if (size > cabe) size = cabe;
        saida.insert(saida.end(), buffer, buffer + size);
        return size;
    }
    using Print::write;

    int parsePacket() override
    {
        if (id < 0) return 0;
        tamanho = posicao = 0;
        if (cs_obj_i(nullptr, "__udp_esperando", id) <= 0) return 0;
        int n = (int)cs_obj_i(nullptr, "__udp_receber", id, cs_vetor_mutavel(entrada, (int)sizeof(entrada)));
        if (n < 0) return 0;
        tamanho = (size_t)n;
        String quem = cs_obj_t(nullptr, "__udp_remetente", id);
        int dois = quem.lastIndexOf(':');
        ipRemoto.fromString(quem.substring(0, dois));
        portaRemota = (uint16_t)quem.substring(dois + 1).toInt();
        return n;
    }
    int available() override { return (int)(tamanho - posicao); }
    int read() override { return posicao < tamanho ? entrada[posicao++] : -1; }
    int read(unsigned char *buffer, size_t len) override
    {
        size_t n = 0;
        while (n < len && posicao < tamanho) buffer[n++] = entrada[posicao++];
        return (int)n;
    }
    int read(char *buffer, size_t len) override { return read((unsigned char *)buffer, len); }
    int peek() override { return posicao < tamanho ? entrada[posicao] : -1; }
    void clear() { posicao = tamanho; }
    void flush() override { clear(); }
    IPAddress remoteIP() override { return ipRemoto; }
    uint16_t remotePort() override { return portaRemota; }
};

typedef NetworkUDP WiFiUDP;

#endif
