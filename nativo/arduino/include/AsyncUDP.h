/* AsyncUDP.h -- o UDP por eventos da ESP32: o onPacket() chama a funcao a
 * cada pacote que chega, numa tarefa do FreeRTOS (no core oficial, a do
 * lwIP). Por baixo, o mesmo UDP do WiFiUdp.h (a rede do simulador). */
#ifndef ESPASYNCUDP_H
#define ESPASYNCUDP_H

#include "Arduino.h"
#include "IPAddress.h"
#include "Print.h"
#include "Stream.h"
#include "cs_objeto.h"
#include <functional>
#include <vector>
#include <algorithm>

#ifndef CONFIG_TCP_MSS
#define CONFIG_TCP_MSS 1436
#endif

typedef enum {
    TCPIP_ADAPTER_IF_STA = 0,
    TCPIP_ADAPTER_IF_AP,
    TCPIP_ADAPTER_IF_ETH,
    TCPIP_ADAPTER_IF_PPP,
    TCPIP_ADAPTER_IF_MAX
} tcpip_adapter_if_t;

#ifndef CS_IP_ADDR_T
#define CS_IP_ADDR_T
/* O endereco do lwIP (so IPv4 aqui), na ordem da rede. */
typedef struct ip4_addr {
    uint32_t addr;
} ip4_addr_t;
typedef ip4_addr_t ip_addr_t;
#endif

class AsyncUDP;
class AsyncUDPPacket;
class AsyncUDPMessage;

typedef std::function<void(AsyncUDPPacket &packet)> AuPacketHandlerFunction;
typedef std::function<void(void *arg, AsyncUDPPacket &packet)> AuPacketHandlerFunctionWithArg;

class AsyncUDPMessage : public Print {
protected:
    uint8_t *_buffer;
    size_t _index;
    size_t _size;

public:
    AsyncUDPMessage(size_t size = CONFIG_TCP_MSS) : _buffer((uint8_t *)malloc(size)), _index(0), _size(_buffer ? size : 0) {}
    virtual ~AsyncUDPMessage() { free(_buffer); }
    size_t write(const uint8_t *data, size_t len) override
    {
        size_t s = space();
        if (len > s) len = s;
        memcpy(_buffer + _index, data, len);
        _index += len;
        return len;
    }
    size_t write(uint8_t data) override { return write(&data, 1); }
    using Print::write;
    size_t space() { return _size - _index; }
    uint8_t *data() { return _buffer; }
    size_t length() { return _index; }
    void flush() override { _index = 0; }
    operator bool() { return _buffer != NULL; }
};

class AsyncUDPPacket : public Stream {
protected:
    AsyncUDP *_udp;
    std::vector<uint8_t> _dados;
    IPAddress _remoteIp;
    uint16_t _remotePort;
    uint16_t _localPort;
    size_t _index;

public:
    AsyncUDPPacket(AsyncUDP *udp, const uint8_t *data, size_t len, IPAddress ip, uint16_t port, uint16_t local)
        : _udp(udp), _dados(data, data + len), _remoteIp(ip), _remotePort(port), _localPort(local), _index(0) {}
    AsyncUDPPacket(AsyncUDPPacket &p) = default;
    AsyncUDPPacket &operator=(const AsyncUDPPacket &p) = default;
    virtual ~AsyncUDPPacket() {}

    uint8_t *data() { return _dados.data(); }
    size_t length() { return _dados.size(); }
    bool isBroadcast() { return _remoteIp[3] == 255 || (uint32_t)localIP() == 0xFFFFFFFF; }
    bool isMulticast() { return false; }
    bool isIPv6() { return false; }
    tcpip_adapter_if_t interface() { return TCPIP_ADAPTER_IF_STA; }
    IPAddress localIP();
    uint16_t localPort() { return _localPort; }
    IPAddress remoteIP() { return _remoteIp; }
    uint16_t remotePort() { return _remotePort; }
    void remoteMac(uint8_t *mac) { memset(mac, 0, 6); }
    size_t send(AsyncUDPMessage &message);

    int available() override { return (int)(_dados.size() - _index); }
    size_t read(uint8_t *data, size_t len)
    {
        size_t n = std::min(len, _dados.size() - _index);
        memcpy(data, _dados.data() + _index, n);
        _index += n;
        return n;
    }
    int read() override { return _index < _dados.size() ? _dados[_index++] : -1; }
    int peek() override { return _index < _dados.size() ? _dados[_index] : -1; }
    void flush() override { _index = _dados.size(); }
    /* Escrever num pacote recebido responde a quem mandou. */
    size_t write(const uint8_t *data, size_t len) override;
    size_t write(uint8_t data) override { return write(&data, 1); }
    using Print::write;
};

class AsyncUDP : public Print {
protected:
    int _id = -1;
    uint16_t _porta = 0;
    bool _connected = false;
    esp_err_t _lastErr = 0;
    IPAddress _alvo;
    uint16_t _portaAlvo = 0;
    IPAddress _escuta;
    AuPacketHandlerFunction _handler;

    static std::vector<AsyncUDP *> &todos()
    {
        static auto *v = new std::vector<AsyncUDP *>();
        return *v;
    }
    /* A tarefa que entrega os pacotes (a do lwIP, na placa). */
    static void tarefa(void *)
    {
        uint8_t buf[1460];
        for (;;) {
            for (size_t i = 0; i < todos().size(); i++) {
                AsyncUDP *u = todos()[i];
                if (u->_id < 0 || !u->_handler) continue;
                while (cs_obj_i(nullptr, "__udp_esperando", u->_id) > 0) {
                    int n = (int)cs_obj_i(nullptr, "__udp_receber", u->_id, cs_vetor_mutavel(buf, (int)sizeof(buf)));
                    if (n < 0) break;
                    String quem = cs_obj_t(nullptr, "__udp_remetente", u->_id);
                    int dois = quem.lastIndexOf(':');
                    IPAddress ip;
                    ip.fromString(quem.substring(0, dois));
                    AsyncUDPPacket p(u, buf, (size_t)n, ip, (uint16_t)quem.substring(dois + 1).toInt(), u->_porta);
                    u->_handler(p);
                }
            }
            vTaskDelay(1);
        }
    }
    bool _init()
    {
        if (_id >= 0) return true;
        static bool comecou = false;
        if (!comecou) {
            comecou = true;
            xTaskCreate(tarefa, "async_udp", 4096, nullptr, 3, nullptr);
        }
        if (std::find(todos().begin(), todos().end(), this) == todos().end()) todos().push_back(this);
        return true;
    }
    bool abrir(uint16_t porta)
    {
        _init();
        if (_id >= 0) cs_obj_n(nullptr, "__udp_fechar", _id);
        _id = (int)cs_obj_i(nullptr, "__udp_abrir", (int)porta);
        if (_id < 0) {
            _lastErr = -1;
            return false;
        }
        _porta = (uint16_t)cs_obj_i(nullptr, "__udp_porta", _id);
        return true;
    }
    static IPAddress ipDe(const ip_addr_t *a) { return a ? IPAddress(a->addr) : IPAddress(); }

public:
    AsyncUDP() {}
    virtual ~AsyncUDP()
    {
        close();
        auto &v = todos();
        v.erase(std::remove(v.begin(), v.end(), this), v.end());
    }

    void onPacket(AuPacketHandlerFunctionWithArg cb, void *arg = NULL)
    {
        onPacket([cb, arg](AsyncUDPPacket &p) { cb(arg, p); });
    }
    void onPacket(AuPacketHandlerFunction cb) { _handler = cb; }

    bool listen(const ip_addr_t *addr, uint16_t port) { return listen(ipDe(addr), port); }
    bool listen(const IPAddress addr, uint16_t port)
    {
        _escuta = addr;
        return abrir(port);
    }
    bool listen(uint16_t port) { return listen(IPAddress(), port); }
    bool listenMulticast(const ip_addr_t *addr, uint16_t port, uint8_t ttl = 1, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return listenMulticast(ipDe(addr), port, ttl, tcpip_if);
    }
    bool listenMulticast(const IPAddress addr, uint16_t port, uint8_t ttl = 1, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        (void)ttl;
        (void)tcpip_if;
        if (!abrir(port)) return false;
        _escuta = addr;
        cs_obj_n(nullptr, "__udp_grupo", _id, addr.toString());
        return true;
    }
    bool connect(const ip_addr_t *addr, uint16_t port) { return connect(ipDe(addr), port); }
    bool connect(const IPAddress addr, uint16_t port)
    {
        if (_id < 0 && !abrir(0)) return false;
        _alvo = addr;
        _portaAlvo = port;
        _connected = true;
        return true;
    }
    void close()
    {
        if (_id >= 0) cs_obj_n(nullptr, "__udp_fechar", _id);
        _id = -1;
        _connected = false;
    }

    size_t writeTo(const uint8_t *data, size_t len, const ip_addr_t *addr, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return writeTo(data, len, ipDe(addr), port, tcpip_if);
    }
    size_t writeTo(const uint8_t *data, size_t len, const IPAddress addr, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        (void)tcpip_if;
        if (_id < 0 && !abrir(0)) return 0;
        if (len > CONFIG_TCP_MSS) len = CONFIG_TCP_MSS;
        bool foi = cs_obj_i(nullptr, "__udp_enviar", _id, addr.toString(), (int)port, cs_vetor(data, (int)len)) != 0;
        _lastErr = foi ? 0 : -1;
        return foi ? len : 0;
    }
    size_t write(const uint8_t *data, size_t len) override { return _connected ? writeTo(data, len, _alvo, _portaAlvo) : 0; }
    size_t write(uint8_t data) override { return write(&data, 1); }
    using Print::write;
    size_t broadcastTo(uint8_t *data, size_t len, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return writeTo(data, len, IPAddress(255, 255, 255, 255), port, tcpip_if);
    }
    size_t broadcastTo(const char *data, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return broadcastTo((uint8_t *)data, strlen(data), port, tcpip_if);
    }
    size_t broadcast(uint8_t *data, size_t len) { return _porta ? broadcastTo(data, len, _porta) : 0; }
    size_t broadcast(const char *data) { return broadcast((uint8_t *)data, strlen(data)); }
    size_t sendTo(AsyncUDPMessage &message, const ip_addr_t *addr, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return writeTo(message.data(), message.length(), addr, port, tcpip_if);
    }
    size_t sendTo(AsyncUDPMessage &message, const IPAddress addr, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return writeTo(message.data(), message.length(), addr, port, tcpip_if);
    }
    size_t send(AsyncUDPMessage &message) { return write(message.data(), message.length()); }
    size_t broadcastTo(AsyncUDPMessage &message, uint16_t port, tcpip_adapter_if_t tcpip_if = TCPIP_ADAPTER_IF_MAX)
    {
        return broadcastTo(message.data(), message.length(), port, tcpip_if);
    }
    size_t broadcast(AsyncUDPMessage &message) { return broadcast(message.data(), message.length()); }
    IPAddress listenIP() { return _escuta; }
    bool connected() { return _connected; }
    esp_err_t lastErr() { return _lastErr; }
    operator bool() { return _connected; }
    uint16_t cs_porta() const { return _porta; }
};

inline IPAddress AsyncUDPPacket::localIP() { return IPAddress(); }
inline size_t AsyncUDPPacket::send(AsyncUDPMessage &message) { return write(message.data(), message.length()); }
inline size_t AsyncUDPPacket::write(const uint8_t *data, size_t len) { return _udp ? _udp->writeTo(data, len, _remoteIp, _remotePort) : 0; }

#endif
