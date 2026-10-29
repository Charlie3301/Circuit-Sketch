/* WebSocketsServer.h -- o servidor WebSocket da biblioteca "WebSockets" (Markus
 * Sattler, a do Gerenciador de Bibliotecas), com o protocolo de verdade: o
 * aperto de mao (SHA-1 e base64 da chave), os quadros de texto e binario, o
 * ping/pong e o fechamento. Roda sobre o WiFiServer do simulador -- o
 * navegador do computador conecta pela ponte em localhost, na mesma porta da
 * placa quando ela esta livre (o ws://localhost:81 da pagina funciona). */
#ifndef WEBSOCKETSSERVER_H_
#define WEBSOCKETSSERVER_H_

#include "WiFi.h"
#include "WiFiClient.h"
#include <functional>
#include <string.h>

#ifndef WEBSOCKETS_SERVER_CLIENT_MAX
#define WEBSOCKETS_SERVER_CLIENT_MAX 5
#endif

typedef enum {
    WStype_ERROR,
    WStype_DISCONNECTED,
    WStype_CONNECTED,
    WStype_TEXT,
    WStype_BIN,
    WStype_FRAGMENT_TEXT_START,
    WStype_FRAGMENT_BIN_START,
    WStype_FRAGMENT,
    WStype_FRAGMENT_FIN,
    WStype_PING,
    WStype_PONG,
} WStype_t;

typedef enum { WSop_continuation = 0x00, WSop_text = 0x01, WSop_binary = 0x02, WSop_close = 0x08, WSop_ping = 0x09, WSop_pong = 0x0A } WSopcode_t;

/* SHA-1 e base64: so o que o aperto de mao precisa. */
namespace cs_ws {
inline void sha1(const uint8_t *dados, size_t n, uint8_t saida[20])
{
    uint32_t h[5] = {0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0};
    size_t total = ((n + 8) / 64 + 1) * 64;
    uint8_t *m = new uint8_t[total]();
    memcpy(m, dados, n);
    m[n] = 0x80;
    uint64_t bits = (uint64_t)n * 8;
    for (int i = 0; i < 8; i++) m[total - 1 - i] = (uint8_t)(bits >> (8 * i));
    auto rol = [](uint32_t v, int b) { return (v << b) | (v >> (32 - b)); };
    for (size_t bloco = 0; bloco < total; bloco += 64) {
        uint32_t w[80];
        for (int i = 0; i < 16; i++)
            w[i] = (uint32_t)m[bloco + 4 * i] << 24 | (uint32_t)m[bloco + 4 * i + 1] << 16 | (uint32_t)m[bloco + 4 * i + 2] << 8 | m[bloco + 4 * i + 3];
        for (int i = 16; i < 80; i++) w[i] = rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
        uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4];
        for (int i = 0; i < 80; i++) {
            uint32_t f, k;
            if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
            else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
            else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
            else { f = b ^ c ^ d; k = 0xCA62C1D6; }
            uint32_t t = rol(a, 5) + f + e + k + w[i];
            e = d; d = c; c = rol(b, 30); b = a; a = t;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e;
    }
    delete[] m;
    for (int i = 0; i < 5; i++)
        for (int j = 0; j < 4; j++) saida[4 * i + j] = (uint8_t)(h[i] >> (24 - 8 * j));
}

inline String base64(const uint8_t *d, size_t n)
{
    static const char *t = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    String r;
    for (size_t i = 0; i < n; i += 3) {
        uint32_t v = (uint32_t)d[i] << 16 | (i + 1 < n ? (uint32_t)d[i + 1] << 8 : 0) | (i + 2 < n ? d[i + 2] : 0);
        r += t[(v >> 18) & 63];
        r += t[(v >> 12) & 63];
        r += i + 1 < n ? t[(v >> 6) & 63] : '=';
        r += i + 2 < n ? t[v & 63] : '=';
    }
    return r;
}
} // namespace cs_ws

class WebSocketsServer {
public:
    typedef std::function<void(uint8_t num, WStype_t type, uint8_t *payload, size_t length)> WebSocketServerEvent;
    typedef std::function<bool(String headerName, String headerValue)> WebSocketServerHttpHeaderValFunc;

    WebSocketsServer(uint16_t port, const String &origin = "", const String &protocol = "arduino")
        : servidor(port), porta(port), protocolo(protocol)
    {
        (void)origin;
    }
    virtual ~WebSocketsServer() {}

    void begin() { servidor.begin(); }
    void close()
    {
        disconnect();
        servidor.close();
    }

    void onEvent(WebSocketServerEvent cb) { evento = cb; }
    void onValidateHttpHeader(WebSocketServerHttpHeaderValFunc, const char **, uint32_t) {}
    void setAuthorization(const char *, const char *) {}
    void setAuthorization(const char *) {}
    void enableHeartbeat(uint32_t, uint32_t, uint8_t) {}
    void disableHeartbeat() {}

    /* O que chegou: conexoes novas, apertos de mao e quadros. */
    void loop()
    {
        WiFiClient novo = servidor.accept();
        if (novo) {
            int livre = -1;
            for (int i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX && livre < 0; i++)
                if (!clientes[i].estado) livre = i;
            if (livre < 0) {
                novo.print("HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\n\r\n");
                novo.stop();
            } else {
                clientes[livre].tcp = novo;
                clientes[livre].estado = 1;
                clientes[livre].entrada = "";
                clientes[livre].bytes.clear();
            }
        }
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++) atender(i);
    }

    bool sendTXT(uint8_t num, const uint8_t *payload, size_t length = 0, bool = false)
    {
        return enviar(num, WSop_text, payload, length ? length : strlen((const char *)payload));
    }
    bool sendTXT(uint8_t num, const char *payload, size_t length = 0, bool h = false) { return sendTXT(num, (const uint8_t *)payload, length, h); }
    bool sendTXT(uint8_t num, const String &payload) { return sendTXT(num, (const uint8_t *)payload.c_str(), payload.length()); }
    bool sendTXT(uint8_t num, char *payload, size_t length = 0, bool h = false) { return sendTXT(num, (const uint8_t *)payload, length, h); }

    bool broadcastTXT(const uint8_t *payload, size_t length = 0, bool = false)
    {
        bool algum = false;
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++)
            if (clientes[i].estado == 2) algum |= sendTXT(i, payload, length);
        return algum;
    }
    bool broadcastTXT(const char *payload, size_t length = 0, bool h = false) { return broadcastTXT((const uint8_t *)payload, length, h); }
    bool broadcastTXT(const String &payload) { return broadcastTXT((const uint8_t *)payload.c_str(), payload.length()); }
    bool broadcastTXT(char *payload, size_t length = 0, bool h = false) { return broadcastTXT((const uint8_t *)payload, length, h); }

    bool sendBIN(uint8_t num, const uint8_t *payload, size_t length, bool = false) { return enviar(num, WSop_binary, payload, length); }
    bool broadcastBIN(const uint8_t *payload, size_t length, bool = false)
    {
        bool algum = false;
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++)
            if (clientes[i].estado == 2) algum |= sendBIN(i, payload, length);
        return algum;
    }

    bool sendPing(uint8_t num, const uint8_t *payload = nullptr, size_t length = 0) { return enviar(num, WSop_ping, payload, length); }
    bool sendPing(uint8_t num, const String &payload) { return sendPing(num, (const uint8_t *)payload.c_str(), payload.length()); }
    bool broadcastPing(const uint8_t *payload = nullptr, size_t length = 0)
    {
        bool algum = false;
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++)
            if (clientes[i].estado == 2) algum |= sendPing(i, payload, length);
        return algum;
    }

    void disconnect()
    {
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++) disconnect(i);
    }
    void disconnect(uint8_t num)
    {
        if (num >= WEBSOCKETS_SERVER_CLIENT_MAX || !clientes[num].estado) return;
        if (clientes[num].estado == 2) enviar(num, WSop_close, nullptr, 0);
        fechar(num);
    }

    int connectedClients(bool ping = false)
    {
        (void)ping;
        int n = 0;
        for (uint8_t i = 0; i < WEBSOCKETS_SERVER_CLIENT_MAX; i++)
            if (clientes[i].estado == 2) n++;
        return n;
    }

    bool clientIsConnected(uint8_t num) { return num < WEBSOCKETS_SERVER_CLIENT_MAX && clientes[num].estado == 2; }
    IPAddress remoteIP(uint8_t num) { return num < WEBSOCKETS_SERVER_CLIENT_MAX ? clientes[num].tcp.remoteIP() : IPAddress(); }

private:
    struct Cliente {
        WiFiClient tcp{nullptr};
        int estado = 0;           /* 0 livre, 1 esperando o aperto de mao, 2 conectado */
        String entrada;           /* o pedido HTTP chegando */
        std::string bytes;        /* os quadros chegando */
        std::string pedaco;       /* a mensagem em pedacos (FIN = 0) */
        uint8_t tipoDoPedaco = 0;
    };

    WiFiServer servidor;
    uint16_t porta;
    String protocolo;
    WebSocketServerEvent evento;
    Cliente clientes[WEBSOCKETS_SERVER_CLIENT_MAX];

    void avisar(uint8_t num, WStype_t tipo, uint8_t *dados, size_t n)
    {
        if (evento) evento(num, tipo, dados, n);
    }

    void fechar(uint8_t num)
    {
        Cliente &c = clientes[num];
        bool estava = c.estado == 2;
        c.tcp.stop();
        c.tcp = WiFiClient(nullptr);
        c.estado = 0;
        c.bytes.clear();
        c.pedaco.clear();
        if (estava) avisar(num, WStype_DISCONNECTED, nullptr, 0);
    }

    bool enviar(uint8_t num, uint8_t op, const uint8_t *dados, size_t n)
    {
        if (num >= WEBSOCKETS_SERVER_CLIENT_MAX || clientes[num].estado != 2) return false;
        uint8_t cab[10];
        size_t c = 0;
        cab[c++] = 0x80 | op;
        if (n < 126) cab[c++] = (uint8_t)n;
        else if (n < 65536) { cab[c++] = 126; cab[c++] = (uint8_t)(n >> 8); cab[c++] = (uint8_t)n; }
        else { cab[c++] = 127; for (int i = 7; i >= 0; i--) cab[c++] = (uint8_t)((uint64_t)n >> (8 * i)); }
        std::string quadro((const char *)cab, c);
        if (n) quadro.append((const char *)dados, n);
        return clientes[num].tcp.write((const uint8_t *)quadro.data(), quadro.size()) == quadro.size();
    }

    void atender(uint8_t num)
    {
        Cliente &c = clientes[num];
        if (!c.estado) return;
        if (!c.tcp.connected() && c.tcp.available() <= 0) {
            fechar(num);
            return;
        }
        if (c.estado == 1) {
            while (c.tcp.available() > 0) c.entrada += (char)c.tcp.read();
            int fim = c.entrada.indexOf("\r\n\r\n");
            if (fim < 0) return;
            apertarMao(num, c.entrada.substring(0, fim));
            return;
        }
        int n = c.tcp.available();
        if (n > 0) {
            std::string chegou((size_t)n, '\0');
            n = c.tcp.read((uint8_t *)&chegou[0], (size_t)n);
            c.bytes.append(chegou, 0, (size_t)(n > 0 ? n : 0));
        }
        while (c.estado == 2 && lerQuadro(num)) {}
    }

    void apertarMao(uint8_t num, const String &pedido)
    {
        Cliente &c = clientes[num];
        String chave, caminho = "/";
        int sp1 = pedido.indexOf(' '), sp2 = sp1 >= 0 ? pedido.indexOf(' ', sp1 + 1) : -1;
        if (sp1 >= 0 && sp2 > sp1) caminho = pedido.substring(sp1 + 1, sp2);
        int linha = 0;
        while (linha >= 0 && linha < (int)pedido.length()) {
            int proxima = pedido.indexOf("\r\n", linha);
            String h = proxima < 0 ? pedido.substring(linha) : pedido.substring(linha, proxima);
            String baixo = h;
            baixo.toLowerCase();
            if (baixo.startsWith("sec-websocket-key:")) {
                chave = h.substring(h.indexOf(':') + 1);
                chave.trim();
            }
            linha = proxima < 0 ? -1 : proxima + 2;
        }
        if (!chave.length()) {
            c.tcp.print("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\nThis is a Websocket server only!");
            c.estado = 0;
            c.tcp.stop();
            c.tcp = WiFiClient(nullptr);
            return;
        }
        String juntos = chave + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        uint8_t resumo[20];
        cs_ws::sha1((const uint8_t *)juntos.c_str(), juntos.length(), resumo);
        c.tcp.print("HTTP/1.1 101 Switching Protocols\r\nServer: arduino-WebSocketsServer\r\nUpgrade: websocket\r\n"
                    "Connection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Accept: " +
                    cs_ws::base64(resumo, 20) + "\r\n\r\n");
        c.estado = 2;
        c.bytes.clear();
        avisar(num, WStype_CONNECTED, (uint8_t *)caminho.c_str(), caminho.length());
    }

    /* Um quadro inteiro do buffer (o navegador manda mascarado); falso se falta chegar. */
    bool lerQuadro(uint8_t num)
    {
        Cliente &c = clientes[num];
        const std::string &b = c.bytes;
        if (b.size() < 2) return false;
        uint8_t b0 = (uint8_t)b[0], b1 = (uint8_t)b[1];
        bool fin = b0 & 0x80;
        uint8_t op = b0 & 0x0F;
        bool mascarado = b1 & 0x80;
        uint64_t n = b1 & 0x7F;
        size_t p = 2;
        if (n == 126) {
            if (b.size() < 4) return false;
            n = (uint64_t)(uint8_t)b[2] << 8 | (uint8_t)b[3];
            p = 4;
        } else if (n == 127) {
            if (b.size() < 10) return false;
            n = 0;
            for (int i = 0; i < 8; i++) n = n << 8 | (uint8_t)b[2 + i];
            p = 10;
        }
        uint8_t mascara[4] = {0, 0, 0, 0};
        if (mascarado) {
            if (b.size() < p + 4) return false;
            memcpy(mascara, b.data() + p, 4);
            p += 4;
        }
        if (b.size() < p + n) return false;
        std::string dados = b.substr(p, (size_t)n);
        for (size_t i = 0; i < dados.size(); i++) dados[i] ^= mascara[i & 3];
        c.bytes.erase(0, p + (size_t)n);

        switch (op) {
        case WSop_text:
        case WSop_binary:
            if (!fin) {
                c.pedaco = dados;
                c.tipoDoPedaco = op;
                avisar(num, op == WSop_text ? WStype_FRAGMENT_TEXT_START : WStype_FRAGMENT_BIN_START, (uint8_t *)&dados[0], dados.size());
                break;
            }
            dados.push_back('\0');
            avisar(num, op == WSop_text ? WStype_TEXT : WStype_BIN, (uint8_t *)&dados[0], dados.size() - 1);
            break;
        case WSop_continuation:
            c.pedaco += dados;
            avisar(num, fin ? WStype_FRAGMENT_FIN : WStype_FRAGMENT, (uint8_t *)&dados[0], dados.size());
            if (fin) {
                c.pedaco.push_back('\0');
                avisar(num, c.tipoDoPedaco == WSop_text ? WStype_TEXT : WStype_BIN, (uint8_t *)&c.pedaco[0], c.pedaco.size() - 1);
                c.pedaco.clear();
            }
            break;
        case WSop_ping:
            enviar(num, WSop_pong, (const uint8_t *)dados.data(), dados.size());
            avisar(num, WStype_PING, (uint8_t *)&dados[0], dados.size());
            break;
        case WSop_pong:
            avisar(num, WStype_PONG, (uint8_t *)&dados[0], dados.size());
            break;
        case WSop_close:
            enviar(num, WSop_close, (const uint8_t *)dados.data(), dados.size() < 2 ? dados.size() : 2);
            fechar(num);
            return false;
        }
        return true;
    }
};

#endif
