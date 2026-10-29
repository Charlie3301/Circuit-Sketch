/* WiFiClientSecure.h -- o cliente HTTPS: o mesmo cliente, com os certificados. */
#ifndef _WIFICLIENTSECURE_H_
#define _WIFICLIENTSECURE_H_

#include "WiFiClient.h"

class WiFiClientSecure : public WiFiClient {
public:
    WiFiClientSecure() : WiFiClient("WiFiClientSecure", 0) {}
    void setInsecure() { cs_obj_n(cs_nome, "setInsecure"); }
    void setCACert(const char *rootCA) { cs_obj_n(cs_nome, "setCACert", rootCA); }
    void setCertificate(const char *client_ca) { cs_obj_n(cs_nome, "setCertificate", client_ca); }
    void setPrivateKey(const char *private_key) { cs_obj_n(cs_nome, "setPrivateKey", private_key); }
};

#endif
