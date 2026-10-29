/* PubSubClient.h -- o MQTT do Nick O'Leary: o broker da bancada (ou o da
 * internet), e o callback do sketch chamado com o topico e a mensagem. */
#ifndef PubSubClient_h
#define PubSubClient_h

#include "cs_objeto.h"
#include "WiFiClient.h"
#include "IPAddress.h"

#define MQTT_VERSION_3_1 3
#define MQTT_VERSION_3_1_1 4
#define MQTT_VERSION MQTT_VERSION_3_1_1
#define MQTT_MAX_PACKET_SIZE 256
#define MQTT_KEEPALIVE 15
#define MQTT_SOCKET_TIMEOUT 15

#define MQTT_CONNECTION_TIMEOUT -4
#define MQTT_CONNECTION_LOST -3
#define MQTT_CONNECT_FAILED -2
#define MQTT_DISCONNECTED -1
#define MQTT_CONNECTED 0
#define MQTT_CONNECT_BAD_PROTOCOL 1
#define MQTT_CONNECT_BAD_CLIENT_ID 2
#define MQTT_CONNECT_UNAVAILABLE 3
#define MQTT_CONNECT_BAD_CREDENTIALS 4
#define MQTT_CONNECT_UNAUTHORIZED 5

#define MQTT_CALLBACK_SIGNATURE std::function<void(char *, uint8_t *, unsigned int)> callback

class PubSubClient : public CsObjeto {
public:
    PubSubClient() { cs_criar("PubSubClient"); }
    PubSubClient(Client &client) { cs_criar("PubSubClient", cs_de_cliente(client)); }
    PubSubClient(const char *domain, uint16_t port, Client &client) { cs_criar("PubSubClient", cs_de_cliente(client)); setServer(domain, port); }
    PubSubClient(IPAddress ip, uint16_t port, Client &client) { cs_criar("PubSubClient", cs_de_cliente(client)); setServer(ip, port); }

    PubSubClient &setServer(const char *domain, uint16_t port) { cs_obj_n(cs_nome, "setServer", domain, port); return *this; }
    PubSubClient &setServer(IPAddress ip, uint16_t port) { cs_obj_n(cs_nome, "setServer", ip.toString(), port); return *this; }
    PubSubClient &setServer(uint8_t *ip, uint16_t port) { return setServer(IPAddress(ip), port); }
    PubSubClient &setCallback(std::function<void(char *, uint8_t *, unsigned int)> callback)
    {
        cs_obj_n(cs_nome, "setCallback", callback);
        return *this;
    }
    PubSubClient &setCallback(void (*callback)(char *, uint8_t *, unsigned int))
    {
        cs_obj_n(cs_nome, "setCallback", callback);
        return *this;
    }
    PubSubClient &setClient(Client &client) { cs_obj_n(cs_nome, "setClient", cs_de_cliente(client)); return *this; }
    PubSubClient &setKeepAlive(uint16_t keepAlive) { cs_obj_n(cs_nome, "setKeepAlive", keepAlive); return *this; }
    PubSubClient &setSocketTimeout(uint16_t timeout) { cs_obj_n(cs_nome, "setSocketTimeout", timeout); return *this; }
    bool setBufferSize(uint16_t size) { return cs_obj_i(cs_nome, "setBufferSize", size) != 0; }
    uint16_t getBufferSize() { return (uint16_t)cs_obj_i(cs_nome, "getBufferSize"); }

    bool connect(const char *id) { return cs_obj_i(cs_nome, "connect", id) != 0; }
    bool connect(const char *id, const char *user, const char *pass) { return cs_obj_i(cs_nome, "connect", id, user ? user : "", pass ? pass : "") != 0; }
    bool connect(const char *id, const char *willTopic, uint8_t willQos, bool willRetain, const char *willMessage)
    {
        return cs_obj_i(cs_nome, "connect", id, "", "", willTopic, willQos, willRetain, willMessage) != 0;
    }
    bool connect(const char *id, const char *user, const char *pass, const char *willTopic, uint8_t willQos, bool willRetain,
                 const char *willMessage, bool cleanSession = true)
    {
        (void)cleanSession;
        return cs_obj_i(cs_nome, "connect", id, user ? user : "", pass ? pass : "", willTopic, willQos, willRetain, willMessage) != 0;
    }
    void disconnect() { cs_obj_n(cs_nome, "disconnect"); }
    bool connected() { return cs_obj_i(cs_nome, "connected") != 0; }
    int state() { return (int)cs_obj_i(cs_nome, "state"); }
    bool loop() { return cs_obj_i(cs_nome, "loop") != 0; }

    bool publish(const char *topic, const char *payload) { return cs_obj_i(cs_nome, "publish", topic, payload) != 0; }
    bool publish(const char *topic, const char *payload, bool retained) { return cs_obj_i(cs_nome, "publish", topic, payload, retained) != 0; }
    bool publish(const char *topic, const uint8_t *payload, unsigned int plength)
    {
        return cs_obj_i(cs_nome, "publish", topic, String((const char *)payload, plength)) != 0;
    }
    bool publish(const char *topic, const uint8_t *payload, unsigned int plength, bool retained)
    {
        return cs_obj_i(cs_nome, "publish", topic, String((const char *)payload, plength), retained) != 0;
    }
    bool publish_P(const char *topic, const char *payload, bool retained) { return publish(topic, payload, retained); }
    bool subscribe(const char *topic) { return cs_obj_i(cs_nome, "subscribe", topic) != 0; }
    bool subscribe(const char *topic, uint8_t qos) { return cs_obj_i(cs_nome, "subscribe", topic, qos) != 0; }
    bool unsubscribe(const char *topic) { return cs_obj_i(cs_nome, "unsubscribe", topic) != 0; }

private:
    static cs_val cs_de_cliente(Client &c)
    {
        if (auto *w = dynamic_cast<WiFiClient *>(&c)) return ::cs_nome(w->nomeNoSimulador());
        return ::cs_nome("espClient");
    }
};

#endif
