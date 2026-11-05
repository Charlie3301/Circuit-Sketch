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
  HTTP_CODE_SWITCHING_PROTOCOLS = 101,
  HTTP_CODE_PROCESSING = 102,
  HTTP_CODE_OK = 200,
  HTTP_CODE_CREATED = 201,
  HTTP_CODE_ACCEPTED = 202,
  HTTP_CODE_NON_AUTHORITATIVE_INFORMATION = 203,
  HTTP_CODE_NO_CONTENT = 204,
  HTTP_CODE_RESET_CONTENT = 205,
  HTTP_CODE_PARTIAL_CONTENT = 206,
  HTTP_CODE_MULTI_STATUS = 207,
  HTTP_CODE_ALREADY_REPORTED = 208,
  HTTP_CODE_IM_USED = 226,
  HTTP_CODE_MULTIPLE_CHOICES = 300,
  HTTP_CODE_MOVED_PERMANENTLY = 301,
  HTTP_CODE_FOUND = 302,
  HTTP_CODE_SEE_OTHER = 303,
  HTTP_CODE_NOT_MODIFIED = 304,
  HTTP_CODE_USE_PROXY = 305,
  HTTP_CODE_TEMPORARY_REDIRECT = 307,
  HTTP_CODE_PERMANENT_REDIRECT = 308,
  HTTP_CODE_BAD_REQUEST = 400,
  HTTP_CODE_UNAUTHORIZED = 401,
  HTTP_CODE_PAYMENT_REQUIRED = 402,
  HTTP_CODE_FORBIDDEN = 403,
  HTTP_CODE_NOT_FOUND = 404,
  HTTP_CODE_METHOD_NOT_ALLOWED = 405,
  HTTP_CODE_NOT_ACCEPTABLE = 406,
  HTTP_CODE_PROXY_AUTHENTICATION_REQUIRED = 407,
  HTTP_CODE_REQUEST_TIMEOUT = 408,
  HTTP_CODE_CONFLICT = 409,
  HTTP_CODE_GONE = 410,
  HTTP_CODE_LENGTH_REQUIRED = 411,
  HTTP_CODE_PRECONDITION_FAILED = 412,
  HTTP_CODE_PAYLOAD_TOO_LARGE = 413,
  HTTP_CODE_URI_TOO_LONG = 414,
  HTTP_CODE_UNSUPPORTED_MEDIA_TYPE = 415,
  HTTP_CODE_RANGE_NOT_SATISFIABLE = 416,
  HTTP_CODE_EXPECTATION_FAILED = 417,
  HTTP_CODE_MISDIRECTED_REQUEST = 421,
  HTTP_CODE_UNPROCESSABLE_ENTITY = 422,
  HTTP_CODE_LOCKED = 423,
  HTTP_CODE_FAILED_DEPENDENCY = 424,
  HTTP_CODE_UPGRADE_REQUIRED = 426,
  HTTP_CODE_PRECONDITION_REQUIRED = 428,
  HTTP_CODE_TOO_MANY_REQUESTS = 429,
  HTTP_CODE_REQUEST_HEADER_FIELDS_TOO_LARGE = 431,
  HTTP_CODE_INTERNAL_SERVER_ERROR = 500,
  HTTP_CODE_NOT_IMPLEMENTED = 501,
  HTTP_CODE_BAD_GATEWAY = 502,
  HTTP_CODE_SERVICE_UNAVAILABLE = 503,
  HTTP_CODE_GATEWAY_TIMEOUT = 504,
  HTTP_CODE_HTTP_VERSION_NOT_SUPPORTED = 505,
  HTTP_CODE_VARIANT_ALSO_NEGOTIATES = 506,
  HTTP_CODE_INSUFFICIENT_STORAGE = 507,
  HTTP_CODE_LOOP_DETECTED = 508,
  HTTP_CODE_NOT_EXTENDED = 510,
  HTTP_CODE_NETWORK_AUTHENTICATION_REQUIRED = 511
} t_http_codes;

typedef enum {
  HTTPC_TE_IDENTITY,
  HTTPC_TE_CHUNKED
} transferEncoding_t;

#define HTTPCLIENT_DEFAULT_TCP_TIMEOUT (5000)
#define HTTP_TCP_BUFFER_SIZE (1460)


typedef enum { HTTPC_DISABLE_FOLLOW_REDIRECTS, HTTPC_STRICT_FOLLOW_REDIRECTS, HTTPC_FORCE_FOLLOW_REDIRECTS } followRedirects_t;

class HTTPClient : public CsObjeto {
public:
    HTTPClient() { cs_criar("HTTPClient"); }
    ~HTTPClient();
    bool begin(const String &url)
    {
        _url = url;
        return cs_obj_i(cs_nome, "begin", url) != 0;
    }
    bool begin(const char *url) { return begin(String(url)); }
    bool begin(WiFiClient &client, const String &url)
    {
        _url = url;
        return cs_obj_i(cs_nome, "begin", client, url) != 0;
    }
    bool begin(const String &host, uint16_t port, const String &uri = "/")
    {
        _url = String("http://") + host + ":" + String(port) + uri;
        return cs_obj_i(cs_nome, "begin", host, port, uri) != 0;
    }
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
    void setAcceptEncoding(const String &acceptEncoding) { addHeader("Accept-Encoding", acceptEncoding); }
    void collectAllHeaders(bool = true) {}
    void setReuse(bool reuse) { cs_obj_n(cs_nome, "setReuse", reuse); }
    void setFollowRedirects(followRedirects_t follow) { cs_obj_n(cs_nome, "setFollowRedirects", (int)follow); }
    void collectHeaders(const char *headerKeys[], const size_t headerKeysCount) { (void)headerKeys; (void)headerKeysCount; }
    String header(const char *name) { return cs_obj_t(cs_nome, "header", name); }
    bool hasHeader(const char *name) { return cs_obj_i(cs_nome, "hasHeader", name) != 0; }
    int getSize() { return (int)cs_obj_i(cs_nome, "getSize"); }
    String getString() { return cs_obj_t(cs_nome, "getString"); }

    /* O 3.x da ESP32: os pedidos com o cliente e a URL em partes. */
    bool begin(WiFiClient &client, const String &host, uint16_t port, const String &uri = "/", bool https = false)
    {
        return begin(client, String(https ? "https://" : "http://") + host + ":" + String(port) + uri);
    }
    bool begin(const String &url, const char *CAcert)
    {
        (void)CAcert;
        return begin(url);
    }
    bool begin(const String &host, uint16_t port, const String &uri, const char *CAcert)
    {
        (void)CAcert;
        return begin(String("https://") + host + ":" + String(port) + uri);
    }
    void useHTTP10(bool usehttp10 = true) { _http10 = usehttp10; }
    void setRedirectLimit(uint16_t limit) { _redirecoes = limit; }
    uint16_t getRedirectLimit() { return _redirecoes; }
    bool setURL(const String &url) { return begin(url); }
    String getLocation(void) { return header("Location"); }
    int POST(const String &payload, size_t) { return POST(payload); }
    int PUT(uint8_t *payload, size_t size) { return PUT(String((const char *)payload, (unsigned)size)); }
    int PATCH(uint8_t *payload, size_t size) { return PATCH(String((const char *)payload, (unsigned)size)); }
    int sendRequest(const char *type, uint8_t *payload, size_t size)
    {
        return sendRequest(type, String((const char *)payload, (unsigned)size));
    }
    int sendRequest(const char *type, Stream *stream, size_t size = 0)
    {
        String corpo;
        while (stream && stream->available() && (!size || corpo.length() < size)) corpo += (char)stream->read();
        return sendRequest(type, corpo);
    }
    String header(size_t i) { (void)i; return String(); }
    String headerName(size_t i) { (void)i; return String(); }
    int headers() { return 0; }
    void setAuthorizationType(const char *authType) { (void)authType; }
    void setCookieJar(void *cookieJar) { (void)cookieJar; }
    void resetCookieJar() {}
    void clearAllCookies() {}
    void setInsecure() {}
    void setCACert(const char *) {}

    /* O corpo da resposta como Stream (o getStream/getStreamPtr): os bytes
     * da resposta, inclusive os de um binario (o firmware do HTTPUpdate). */
    WiFiClient *getStreamPtr(void);
    WiFiClient &getStream(void) { return *getStreamPtr(); }
    WiFiClient *getClient(void) { return getStreamPtr(); }
    int writeToStream(Stream *stream)
    {
        WiFiClient *c = getStreamPtr();
        if (!stream || !c) return HTTPC_ERROR_NO_STREAM;
        int n = 0;
        while (c->available()) {
            stream->write((uint8_t)c->read());
            n++;
        }
        return n;
    }

private:
    bool _http10 = false;
    uint16_t _redirecoes = 10;
    WiFiClient *_corpo = nullptr;
    String _url;

public:
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


/* O corpo baixado (os bytes da URL do pedido), lido como um cliente. */
class CsCorpoHttp : public WiFiClient {
    uint8_t *dados = nullptr;
    size_t tamanho = 0, posicao = 0;

public:
    explicit CsCorpoHttp(const String &url) : WiFiClient(nullptr)
    {
        int id = (int)cs_obj_i(nullptr, "__baixar", url);
        if (id < 0) return;
        tamanho = (size_t)cs_obj_i(nullptr, "__baixado_tamanho", id);
        dados = (uint8_t *)malloc(tamanho ? tamanho : 1);
        size_t lido = 0;
        while (lido < tamanho) {
            size_t pedaco = tamanho - lido > 65536 ? 65536 : tamanho - lido;
            int n = (int)cs_obj_i(nullptr, "__baixado_ler", id, (int64_t)lido, cs_vetor_mutavel(dados + lido, (int)pedaco));
            if (n <= 0) break;
            lido += (size_t)n;
        }
        tamanho = lido;
        cs_obj_n(nullptr, "__baixado_soltar", id);
    }
    ~CsCorpoHttp() override { free(dados); }
    int available() override { return (int)(tamanho - posicao); }
    int read() override { return posicao < tamanho ? dados[posicao++] : -1; }
    int read(uint8_t *buf, size_t n) override
    {
        size_t k = 0;
        while (k < n && posicao < tamanho) buf[k++] = dados[posicao++];
        return (int)k;
    }
    size_t readBytes(char *buf, size_t n) override { return (size_t)read((uint8_t *)buf, n); }
    int peek() override { return posicao < tamanho ? dados[posicao] : -1; }
    String readString() override
    {
        String s;
        while (posicao < tamanho) s += (char)dados[posicao++];
        return s;
    }
    String readStringUntil(char terminator) override
    {
        String s;
        while (posicao < tamanho && dados[posicao] != terminator) s += (char)dados[posicao++];
        if (posicao < tamanho) posicao++;
        return s;
    }
    void stop() override {}
    void flush() override {}
    uint8_t connected() override { return posicao < tamanho; }
    operator bool() override { return true; }
};

inline WiFiClient *HTTPClient::getStreamPtr(void)
{
    if (!_corpo) _corpo = new CsCorpoHttp(_url);
    return _corpo;
}
inline HTTPClient::~HTTPClient() { delete _corpo; }

#endif
