/* WiFiClient.h -- o cliente TCP (e o servidor) pela rede do simulador. */
#ifndef _WIFICLIENT_H_
#define _WIFICLIENT_H_

#include "cs_objeto.h"
#include <map>
#include <string>
#include <string.h>
#include "IPAddress.h"

class Client : public Stream {
public:
    virtual int connect(const char *host, uint16_t port) = 0;
    virtual uint8_t connected() = 0;
    virtual void stop() = 0;
    virtual operator bool() = 0;
};

/* Quantas copias de cada client existem: como no core da ESP32 (o socket
 * num shared_ptr), a conexao fecha quando a ultima copia some -- o client
 * guardado (o do WebSocket) fica aberto, o de passagem fecha sozinho. */
inline std::map<std::string, int> &cs_copias_dos_clients()
{
    static auto *copias = new std::map<std::string, int>();
    return *copias;
}

class WiFiClient : public Client, public CsObjeto {
    void segurar()
    {
        if (cs_nome[0]) cs_copias_dos_clients()[cs_nome]++;
    }
    void soltar()
    {
        if (!cs_nome[0]) return;
        auto &copias = cs_copias_dos_clients();
        auto c = copias.find(cs_nome);
        if (c == copias.end() || --c->second > 0) return;
        copias.erase(c);
        cs_obj_n(cs_nome, "stop");
        cs_val nome[] = {cs_t(cs_nome)};
        cs_nada(nullptr, "__destruir", 1, nome);
    }

protected:
    explicit WiFiClient(const char *classe, int)
    {
        cs_criar(classe);
        segurar();
    }

public:
    WiFiClient()
    {
        cs_criar("WiFiClient");
        segurar();
    }
    /* O cliente que o simulador entregou (o server.available()). */
    static WiFiClient adotar(const String &nome)
    {
        WiFiClient c(nullptr);
        c.cs_adotar(nome);
        c.segurar();
        return c;
    }
    explicit WiFiClient(std::nullptr_t) {}
    WiFiClient(const WiFiClient &o) : Client(o), CsObjeto(o) { segurar(); }
    WiFiClient &operator=(const WiFiClient &o)
    {
        if (this == &o || strcmp(cs_nome, o.cs_nome) == 0) return *this;
        soltar();
        CsObjeto::operator=(o);
        segurar();
        return *this;
    }
    ~WiFiClient() override { soltar(); }

    int connect(const char *host, uint16_t port) override { return (int)cs_obj_i(cs_nome, "connect", host, port); }
    int connect(IPAddress ip, uint16_t port) { return (int)cs_obj_i(cs_nome, "connect", ip.toString(), port); }
    int connect(const char *host, uint16_t port, int32_t timeout_ms) { (void)timeout_ms; return connect(host, port); }
    uint8_t connected() override { return (uint8_t)cs_obj_i(cs_nome, "connected"); }
    void stop() override { cs_obj_n(cs_nome, "stop"); }
    operator bool() override { return cs_nome[0] && connected(); }
    int available() override { return (int)cs_obj_i(cs_nome, "available"); }
    int read() override { return (int)cs_obj_i(cs_nome, "read"); }
    int read(uint8_t *buf, size_t size)
    {
        size_t n = 0;
        while (n < size && available() > 0) buf[n++] = (uint8_t)read();
        return (int)n;
    }
    int peek() override { return (int)cs_obj_i(cs_nome, "peek"); }
    void flush() override { cs_obj_n(cs_nome, "flush"); }
    /* Os bytes como estao: o quadro do WebSocket, a imagem, o texto em UTF-8. */
    size_t write(uint8_t c) override { return write(&c, 1); }
    size_t write(const uint8_t *b, size_t n) override
    {
        if (!n || !cs_nome[0]) return 0;
        cs_obj_n(cs_nome, "__escrever_bytes", cs_vetor(b, (int)n));
        return n;
    }
    using Print::write;
    String readString() override { return cs_obj_t(cs_nome, "readString"); }
    String readStringUntil(char terminator) override { return cs_obj_t(cs_nome, "readStringUntil", terminator); }
    IPAddress remoteIP()
    {
        IPAddress ip;
        ip.fromString(cs_obj_t(cs_nome, "remoteIP"));
        return ip;
    }
    uint16_t remotePort() { return (uint16_t)cs_obj_i(cs_nome, "remotePort"); }
    void setTimeout(uint32_t ms) { Stream::setTimeout(ms); cs_obj_n(cs_nome, "setTimeout", ms); }
    int setNoDelay(bool nodelay) { return (int)cs_obj_i(cs_nome, "setNoDelay", nodelay); }
};

class WiFiServer : public CsObjeto {
public:
    WiFiServer(uint16_t port = 80, uint8_t max_clients = 4) { (void)max_clients; cs_criar("WiFiServer", port); }
    void begin(uint16_t port = 0) { if (port) cs_obj_n(cs_nome, "begin", port); else cs_obj_n(cs_nome, "begin"); }
    void end() { cs_obj_n(cs_nome, "end"); }
    void close() { cs_obj_n(cs_nome, "close"); }
    void stop() { cs_obj_n(cs_nome, "stop"); }
    bool hasClient() { return cs_obj_i(cs_nome, "hasClient") != 0; }
    void setNoDelay(bool nodelay) { cs_obj_n(cs_nome, "setNoDelay", nodelay); }
    WiFiClient available() { return WiFiClient::adotar(cs_obj_t(cs_nome, "available")); }
    WiFiClient accept() { return WiFiClient::adotar(cs_obj_t(cs_nome, "accept")); }
};

#endif
