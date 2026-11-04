/* WebServer.h -- o servidor web.
 *
 * Nas placas com o core da ESP32, o WebServer de verdade do arduino-esp32
 * (arduino-esp32/WebServer.h): as rotas, o Uri/UriBraces/UriRegex/UriGlob,
 * os RequestHandler, os middlewares, o serveStatic e o upload, sobre o TCP
 * do simulador (o navegador do computador conecta na placa).
 *
 * Nas outras (o ESP8266WebServer), o do simulador abaixo: o navegador abre
 * as paginas pela rede do simulador, e cada rota chama de volta a funcao do
 * sketch. */
#include "Arduino.h"
#if defined(ESP32)
#include "arduino-esp32/WebServer.h"
#else
#ifndef WEBSERVER_H
#define WEBSERVER_H

#include "cs_objeto.h"
#include "WiFi.h"
#include "Uri.h"
#include <type_traits>

/* O caminho guardado no Uri (o "/led/{}" do UriBraces), para a rota do simulador. */
struct CsCaminhoDoUri : Uri {
    static String de(const Uri &u) { return u.*(&CsCaminhoDoUri::_uri); }
};

typedef enum {
    HTTP_DELETE = 0,
    HTTP_GET = 1,
    HTTP_HEAD = 2,
    HTTP_POST = 3,
    HTTP_PUT = 4,
    HTTP_OPTIONS = 6,
    HTTP_PATCH = 28,
    HTTP_ANY = 127,
} HTTPMethod;

class WebServer : public CsObjeto {
public:
    typedef std::function<void(void)> THandlerFunction;

    WebServer(int port = 80) { cs_criar("WebServer", port); }
    WebServer(IPAddress addr, int port = 80) { cs_criar("WebServer", port); (void)addr; }

    void begin() { cs_obj_n(cs_nome, "begin"); }
    void begin(uint16_t port) { cs_obj_n(cs_nome, "begin", port); }
    void handleClient() { cs_obj_n(cs_nome, "handleClient"); }
    void close() { cs_obj_n(cs_nome, "close"); }
    void stop() { cs_obj_n(cs_nome, "stop"); }

    void on(const String &uri, THandlerFunction fn) { cs_obj_n(cs_nome, "on", uri, fn); }
    void on(const String &uri, HTTPMethod method, THandlerFunction fn) { cs_obj_n(cs_nome, "on", uri, (int)method, fn); }
    void on(const String &uri, HTTPMethod method, THandlerFunction fn, THandlerFunction ufn)
    {
        (void)ufn;
        cs_obj_n(cs_nome, "on", uri, (int)method, fn);
    }
    /* O UriBraces/UriRegex/UriGlob (o texto e o String acima). */
    template <typename U, typename = typename std::enable_if<std::is_base_of<Uri, U>::value>::type>
    void on(const U &uri, THandlerFunction fn) { on(CsCaminhoDoUri::de(uri), fn); }
    template <typename U, typename = typename std::enable_if<std::is_base_of<Uri, U>::value>::type>
    void on(const U &uri, HTTPMethod method, THandlerFunction fn) { on(CsCaminhoDoUri::de(uri), method, fn); }
    void onNotFound(THandlerFunction fn) { cs_obj_n(cs_nome, "onNotFound", fn); }

    String uri() { return cs_obj_t(cs_nome, "uri"); }
    HTTPMethod method() { return (HTTPMethod)cs_obj_i(cs_nome, "method"); }
    String arg(const String &name) { return cs_obj_t(cs_nome, "arg", name); }
    String arg(int i) { return cs_obj_t(cs_nome, "arg", i); }
    String argName(int i) { return cs_obj_t(cs_nome, "argName", i); }
    int args() { return (int)cs_obj_i(cs_nome, "args"); }
    bool hasArg(const String &name) { return cs_obj_i(cs_nome, "hasArg", name) != 0; }
    String pathArg(unsigned int i) { return cs_obj_t(cs_nome, "pathArg", i); }
    String header(const String &name) { return cs_obj_t(cs_nome, "header", name); }
    bool hasHeader(const String &name) { return cs_obj_i(cs_nome, "hasHeader", name) != 0; }
    int headers() { return (int)cs_obj_i(cs_nome, "headers"); }
    void collectHeaders(const char *headerKeys[], const size_t headerKeysCount) { (void)headerKeys; (void)headerKeysCount; }
    bool authenticate(const char *username, const char *password) { return cs_obj_i(cs_nome, "authenticate", username, password) != 0; }
    void requestAuthentication() { cs_obj_n(cs_nome, "requestAuthentication"); }

    void send(int code, const char *content_type = NULL, const String &content = String())
    {
        cs_obj_n(cs_nome, "send", code, content_type ? content_type : "text/html", content);
    }
    void send(int code, const String &content_type, const String &content) { cs_obj_n(cs_nome, "send", code, content_type, content); }
    void send(int code, const char *content_type, const char *content) { cs_obj_n(cs_nome, "send", code, content_type, content); }
    void send_P(int code, PGM_P content_type, PGM_P content) { cs_obj_n(cs_nome, "send", code, content_type, content); }
    void send_P(int code, PGM_P content_type, PGM_P content, size_t contentLength)
    {
        cs_obj_n(cs_nome, "send", code, content_type, String(content, (unsigned)contentLength));
    }
    void sendHeader(const String &name, const String &value, bool first = false) { (void)first; cs_obj_n(cs_nome, "sendHeader", name, value); }
    void setContentLength(size_t contentLength) { cs_obj_n(cs_nome, "setContentLength", (int)contentLength); }
    void sendContent(const String &content) { cs_obj_n(cs_nome, "sendContent", content); }
    void sendContent(const char *content, size_t size) { cs_obj_n(cs_nome, "sendContent", String(content, (unsigned)size)); }
    void sendContent_P(PGM_P content) { cs_obj_n(cs_nome, "sendContent", content); }
    void enableCORS(bool value = true) { cs_obj_n(cs_nome, "enableCORS", value); }
    void enableCrossOrigin(bool value = true) { cs_obj_n(cs_nome, "enableCrossOrigin", value); }
    WiFiClient client() { return WiFiClient(nullptr); }
};

#endif
#endif
