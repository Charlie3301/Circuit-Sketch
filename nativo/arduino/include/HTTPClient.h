/* HTTPClient.h -- o cliente HTTP da ESP32 (e o do ESP8266, pelo
 * ESP8266HTTPClient.h): os pedidos saem pela rede do simulador. */
#ifndef HTTPClient_H_
#define HTTPClient_H_

#include "cs_objeto.h"
#include "WiFiClient.h"

#define HTTPC_ERROR_CONNECTION_REFUSED (-1)
#define HTTPC_ERROR_SEND_HEADER_FAILED (-2)
#define HTTPC_ERROR_SEND_PAYLOAD_FAILED (-3)
#define HTTPC_ERROR_NOT_CONNECTED (-4)
#define HTTPC_ERROR_CONNECTION_LOST (-5)
#define HTTPC_ERROR_NO_STREAM (-6)
#define HTTPC_ERROR_NO_HTTP_SERVER (-7)
#define HTTPC_ERROR_TOO_LESS_RAM (-8)
#define HTTPC_ERROR_ENCODING (-9)
#define HTTPC_ERROR_STREAM_WRITE (-10)
#define HTTPC_ERROR_READ_TIMEOUT (-11)

typedef enum {
    HTTP_CODE_CONTINUE = 100,
    HTTP_CODE_OK = 200,
    HTTP_CODE_CREATED = 201,
    HTTP_CODE_ACCEPTED = 202,
    HTTP_CODE_NO_CONTENT = 204,
    HTTP_CODE_MOVED_PERMANENTLY = 301,
    HTTP_CODE_FOUND = 302,
    HTTP_CODE_NOT_MODIFIED = 304,
    HTTP_CODE_BAD_REQUEST = 400,
    HTTP_CODE_UNAUTHORIZED = 401,
    HTTP_CODE_FORBIDDEN = 403,
    HTTP_CODE_NOT_FOUND = 404,
    HTTP_CODE_INTERNAL_SERVER_ERROR = 500,
} t_http_codes;

typedef enum { HTTPC_DISABLE_FOLLOW_REDIRECTS, HTTPC_STRICT_FOLLOW_REDIRECTS, HTTPC_FORCE_FOLLOW_REDIRECTS } followRedirects_t;

class HTTPClient : public CsObjeto {
public:
    HTTPClient() { cs_criar("HTTPClient"); }
    bool begin(const String &url) { return cs_obj_i(cs_nome, "begin", url) != 0; }
    bool begin(const char *url) { return cs_obj_i(cs_nome, "begin", url) != 0; }
    bool begin(WiFiClient &client, const String &url) { return cs_obj_i(cs_nome, "begin", client, url) != 0; }
    bool begin(const String &host, uint16_t port, const String &uri = "/") { return cs_obj_i(cs_nome, "begin", host, port, uri) != 0; }
    void end() { cs_obj_n(cs_nome, "end"); }
    bool connected() { return cs_obj_i(cs_nome, "connected") != 0; }
    int GET() { return (int)cs_obj_i(cs_nome, "GET"); }
    int POST(const String &payload) { return (int)cs_obj_i(cs_nome, "POST", payload); }
    int POST(const char *payload) { return (int)cs_obj_i(cs_nome, "POST", payload); }
    int POST(uint8_t *payload, size_t size) { return (int)cs_obj_i(cs_nome, "POST", String((const char *)payload, (unsigned)size)); }
    int PUT(const String &payload) { return (int)cs_obj_i(cs_nome, "PUT", payload); }
    int PATCH(const String &payload) { return (int)cs_obj_i(cs_nome, "PATCH", payload); }
    int sendRequest(const char *type, const String &payload = "") { return (int)cs_obj_i(cs_nome, "sendRequest", type, payload); }
    void addHeader(const String &name, const String &value, bool first = false, bool replace = true)
    {
        (void)first;
        (void)replace;
        cs_obj_n(cs_nome, "addHeader", name, value);
    }
    void setAuthorization(const char *user, const char *password) { cs_obj_n(cs_nome, "setAuthorization", user, password); }
    void setAuthorization(const char *auth) { cs_obj_n(cs_nome, "setAuthorization", auth); }
    void setTimeout(uint16_t timeout) { cs_obj_n(cs_nome, "setTimeout", timeout); }
    void setConnectTimeout(int32_t connectTimeout) { cs_obj_n(cs_nome, "setConnectTimeout", connectTimeout); }
    void setUserAgent(const String &userAgent) { cs_obj_n(cs_nome, "setUserAgent", userAgent); }
    void setReuse(bool reuse) { cs_obj_n(cs_nome, "setReuse", reuse); }
    void setFollowRedirects(followRedirects_t follow) { cs_obj_n(cs_nome, "setFollowRedirects", (int)follow); }
    void collectHeaders(const char *headerKeys[], const size_t headerKeysCount) { (void)headerKeys; (void)headerKeysCount; }
    String header(const char *name) { return cs_obj_t(cs_nome, "header", name); }
    bool hasHeader(const char *name) { return cs_obj_i(cs_nome, "hasHeader", name) != 0; }
    int getSize() { return (int)cs_obj_i(cs_nome, "getSize"); }
    String getString() { return cs_obj_t(cs_nome, "getString"); }
    static String errorToString(int error)
    {
        switch (error) {
        case HTTPC_ERROR_CONNECTION_REFUSED: return F("connection refused");
        case HTTPC_ERROR_SEND_HEADER_FAILED: return F("send header failed");
        case HTTPC_ERROR_SEND_PAYLOAD_FAILED: return F("send payload failed");
        case HTTPC_ERROR_NOT_CONNECTED: return F("not connected");
        case HTTPC_ERROR_CONNECTION_LOST: return F("connection lost");
        case HTTPC_ERROR_NO_STREAM: return F("no stream");
        case HTTPC_ERROR_NO_HTTP_SERVER: return F("no HTTP server");
        case HTTPC_ERROR_TOO_LESS_RAM: return F("too less ram");
        case HTTPC_ERROR_ENCODING: return F("Transfer-Encoding not supported");
        case HTTPC_ERROR_STREAM_WRITE: return F("Stream write error");
        case HTTPC_ERROR_READ_TIMEOUT: return F("read Timeout");
        default: return String();
        }
    }
};

#endif
