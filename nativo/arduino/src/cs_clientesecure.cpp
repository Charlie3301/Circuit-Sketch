/* cs_clientesecure.cpp -- o NetworkClientSecure (WiFiClientSecure) da ESP32
 * no simulador: o cabecalho de verdade (NetworkClientSecure.h, ssl_client.h),
 * com a conexao segura feita pela rede do simulador.
 *
 * Na placa o ssl_client.cpp abre o socket do lwIP e negocia o TLS com o
 * mbedTLS; aqui o TLS e o do computador (a conexao de verdade com o
 * servidor), e o certificado que o servidor mandou vai para o mbedTLS de
 * verdade (o mbedtls_ssl_get_peer_cert, o getPeerCertificate, o verify e o
 * getFingerprintSHA256 leem dele). As regras sao as da placa: sem CA, sem
 * PSK, sem o pacote de CAs e sem o setInsecure, o connect falha. */
#include "Arduino.h"
#if defined(ESP32)
#include "NetworkClientSecure.h"
#include <strings.h>
#include "mbedtls/base64.h"
#include "mbedtls/sha256.h"
#include "mbedtls/oid.h"
#include "mbedtls/x509_crt.h"
#include "mbedtls/error.h"

/* =====================================================================
 * as funcoes do ssl_client.h que o resto do core usa */
void ssl_init(sslclient_context *ssl_client)
{
    memset(ssl_client, 0, sizeof(sslclient_context));
    mbedtls_ssl_init(&ssl_client->ssl_ctx);
    mbedtls_ssl_config_init(&ssl_client->ssl_conf);
    mbedtls_ctr_drbg_init(&ssl_client->drbg_ctx);
    ssl_client->socket = -1;
    ssl_client->peek_buf = -1;
}

void attach_ssl_certificate_bundle(sslclient_context *ssl_client, bool att)
{
    /* O pacote de CAs da placa (o esp_crt_bundle): no simulador, os do computador. */
    ssl_client->bundle_attach_cb = att ? (crt_bundle_attach_cb)1 : NULL;
}

void stop_ssl_socket(sslclient_context *ssl_client)
{
    mbedtls_x509_crt_free(&ssl_client->ca_cert);
    mbedtls_x509_crt_free(&ssl_client->client_cert);
    mbedtls_pk_free(&ssl_client->client_key);
    mbedtls_ssl_free(&ssl_client->ssl_ctx);
    mbedtls_ssl_config_free(&ssl_client->ssl_conf);
    mbedtls_ctr_drbg_free(&ssl_client->drbg_ctx);
    mbedtls_entropy_free(&ssl_client->entropy_ctx);
    unsigned long handshake_timeout = ssl_client->handshake_timeout;
    unsigned long socket_timeout = ssl_client->socket_timeout;
    int last_err = ssl_client->last_error;
    crt_bundle_attach_cb bundle_attach_cb = ssl_client->bundle_attach_cb;
    ssl_init(ssl_client);
    ssl_client->handshake_timeout = handshake_timeout;
    ssl_client->socket_timeout = socket_timeout;
    ssl_client->last_error = last_err;
    ssl_client->bundle_attach_cb = bundle_attach_cb;
}

bool get_peer_fingerprint(sslclient_context *ssl_client, uint8_t sha256[32])
{
    if (!ssl_client) return false;
    const mbedtls_x509_crt *crt = mbedtls_ssl_get_peer_cert(&ssl_client->ssl_ctx);
    if (!crt) {
        log_d("Failed to get peer cert.");
        return false;
    }
    mbedtls_sha256(crt->raw.p, crt->raw.len, sha256, 0);
    return true;
}

static bool cs_hex(char c, uint8_t *v)
{
    if (c >= '0' && c <= '9') *v = c - '0';
    else if (c >= 'a' && c <= 'f') *v = c - 'a' + 10;
    else if (c >= 'A' && c <= 'F') *v = c - 'A' + 10;
    else return false;
    return true;
}

/* O nome do servidor bate com o do certificado ("*.exemplo.com" vale para um nivel). */
static bool cs_nome_bate(const char *padrao, size_t n, const char *nome)
{
    if (n > 2 && padrao[0] == '*' && padrao[1] == '.') {
        const char *ponto = strchr(nome, '.');
        return ponto && strlen(ponto) == n - 1 && strncasecmp(ponto, padrao + 1, n - 1) == 0;
    }
    return strlen(nome) == n && strncasecmp(padrao, nome, n) == 0;
}

bool verify_ssl_dn(sslclient_context *ssl_client, const char *domain_name)
{
    const mbedtls_x509_crt *crt = mbedtls_ssl_get_peer_cert(&ssl_client->ssl_ctx);
    if (!crt || !domain_name) return false;
    for (const mbedtls_x509_sequence *san = &crt->subject_alt_names; san; san = san->next)
        if (san->buf.p && cs_nome_bate((const char *)san->buf.p, san->buf.len, domain_name)) return true;
    for (const mbedtls_x509_name *n = &crt->subject; n; n = n->next)
        if (MBEDTLS_OID_CMP(MBEDTLS_OID_AT_CN, &n->oid) == 0 && cs_nome_bate((const char *)n->val.p, n->val.len, domain_name)) return true;
    return false;
}

bool verify_ssl_fingerprint(sslclient_context *ssl_client, const char *fp, const char *domain_name)
{
    uint8_t local[32], remoto[32];
    int len = strlen(fp), pos = 0;
    for (size_t i = 0; i < sizeof(local); ++i) {
        while (pos < len && (fp[pos] == ' ' || fp[pos] == ':')) ++pos;
        uint8_t alto, baixo;
        if (pos > len - 2 || !cs_hex(fp[pos], &alto) || !cs_hex(fp[pos + 1], &baixo)) return false;
        pos += 2;
        local[i] = (uint8_t)(baixo | (alto << 4));
    }
    if (!get_peer_fingerprint(ssl_client, remoto) || memcmp(local, remoto, 32) != 0) return false;
    return domain_name ? verify_ssl_dn(ssl_client, domain_name) : true;
}

/* O certificado que o servidor mandou (o simulador guardou o DER) no
 * mbedTLS, como o handshake deixaria: a sessao do ssl_ctx com o peer_cert. */
static void cs_guardar_certificado(sslclient_context *ssl, const char *nome)
{
    String b64 = cs_obj_t(nome, "__certificado");
    if (!b64.length()) return;
    size_t n = 0;
    mbedtls_base64_decode(NULL, 0, &n, (const unsigned char *)b64.c_str(), b64.length());
    unsigned char *der = (unsigned char *)malloc(n);
    if (!der) return;
    if (mbedtls_base64_decode(der, n, &n, (const unsigned char *)b64.c_str(), b64.length()) == 0) {
        auto *sessao = (mbedtls_ssl_session *)calloc(1, sizeof(mbedtls_ssl_session));
        auto *crt = (mbedtls_x509_crt *)calloc(1, sizeof(mbedtls_x509_crt));
        if (sessao && crt) {
            mbedtls_ssl_session_init(sessao);
            mbedtls_x509_crt_init(crt);
            if (mbedtls_x509_crt_parse_der(crt, der, n) == 0) {
                sessao->MBEDTLS_PRIVATE(peer_cert) = crt;
                ssl->ssl_ctx.MBEDTLS_PRIVATE(session) = sessao;
                crt = NULL;
                sessao = NULL;
            }
        }
        if (crt) {
            mbedtls_x509_crt_free(crt);
            free(crt);
        }
        free(sessao);
    }
    free(der);
}

/* =====================================================================
 * o NetworkClientSecure */
NetworkClientSecure::NetworkClientSecure() : NetworkClient("WiFiClientSecure", 0)
{
    sslclient.reset(new sslclient_context, [](sslclient_context *s) {
        stop_ssl_socket(s);
        delete s;
    });
    ssl_init(sslclient.get());
    sslclient->handshake_timeout = 120000;
    _use_insecure = false;
    _stillinPlainStart = false;
    _ca_cert_free = false;
    _cert_free = false;
    _private_key_free = false;
    _CA_cert = NULL;
    _cert = NULL;
    _private_key = NULL;
    _pskIdent = NULL;
    _psKey = NULL;
    next = NULL;
    _alpn_protos = NULL;
    _cipher_list = NULL;
    _use_ca_bundle = false;
}

/* O socket ja aberto (o servidor seguro da placa): o simulador nao tem. */
NetworkClientSecure::NetworkClientSecure(int socket) : NetworkClientSecure() { (void)socket; }

NetworkClientSecure::~NetworkClientSecure()
{
    if (_ca_cert_free && _CA_cert) free((void *)_CA_cert);
    if (_cert_free && _cert) free((void *)_cert);
    if (_private_key_free && _private_key) free((void *)_private_key);
}

void NetworkClientSecure::stop()
{
    NetworkClient::stop();
    stop_ssl_socket(sslclient.get());
}

int NetworkClientSecure::connect(IPAddress ip, uint16_t port)
{
    if (_pskIdent && _psKey) return connect(ip, port, _pskIdent, _psKey);
    return connect(ip, port, _CA_cert, _cert, _private_key);
}
int NetworkClientSecure::connect(IPAddress ip, uint16_t port, int32_t timeout)
{
    setTimeout(timeout);
    return connect(ip, port);
}
int NetworkClientSecure::connect(const char *host, uint16_t port)
{
    if (_pskIdent && _psKey) return connect(host, port, _pskIdent, _psKey);
    return connect(host, port, _CA_cert, _cert, _private_key);
}
int NetworkClientSecure::connect(const char *host, uint16_t port, int32_t timeout)
{
    setTimeout(timeout);
    return connect(host, port);
}
int NetworkClientSecure::connect(IPAddress ip, uint16_t port, const char *CA_cert, const char *cert, const char *private_key)
{
    return connect(ip, port, NULL, CA_cert, cert, private_key);
}
int NetworkClientSecure::connect(const char *host, uint16_t port, const char *CA_cert, const char *cert, const char *private_key)
{
    IPAddress ip;
    return connect(ip, port, host, CA_cert, cert, private_key);
}
int NetworkClientSecure::connect(IPAddress ip, uint16_t port, const char *pskIdent, const char *psKey)
{
    return connect(ip.toString().c_str(), port, pskIdent, psKey);
}

/* Todas as formas chegam aqui: as regras do start_ssl_client e a conexao pelo simulador. */
static int cs_conectar_seguro(NetworkClient &c, const char *nome, sslclient_context *ssl, const char *host, uint16_t port, bool temCredencial,
                              bool inseguro)
{
    stop_ssl_socket(ssl);
    if (!temCredencial && !inseguro) {
        ssl->last_error = -1;
        log_e("start_ssl_client: connect failed: %d", -1);
        return 0;
    }
    if (inseguro) cs_obj_n(nome, "setInsecure");
    if (!c.NetworkClient::connect(host, port)) {
        ssl->last_error = MBEDTLS_ERR_NET_CONNECT_FAILED;
        log_e("start_ssl_client: connect failed: %d", MBEDTLS_ERR_NET_CONNECT_FAILED);
        return 0;
    }
    ssl->socket = 1;
    ssl->last_error = 0;
    cs_guardar_certificado(ssl, nome);
    return 1;
}

int NetworkClientSecure::connect(IPAddress ip, uint16_t port, const char *host, const char *CA_cert, const char *cert, const char *private_key)
{
    (void)cert;
    (void)private_key;
    String alvo = host ? String(host) : ip.toString();
    if (CA_cert) cs_obj_n(cs_nome, "setCACert", CA_cert);
    return cs_conectar_seguro(*this, cs_nome, sslclient.get(), alvo.c_str(), port, CA_cert || _use_ca_bundle, _use_insecure);
}
int NetworkClientSecure::connect(const char *host, uint16_t port, const char *pskIdent, const char *psKey)
{
    return cs_conectar_seguro(*this, cs_nome, sslclient.get(), host, port, pskIdent && psKey, _use_insecure);
}

int NetworkClientSecure::startTLS()
{
    _stillinPlainStart = false;
    return 1;
}

int NetworkClientSecure::peek() { return NetworkClient::peek(); }
size_t NetworkClientSecure::write(uint8_t data) { return write(&data, 1); }
size_t NetworkClientSecure::write(const uint8_t *buf, size_t size) { return NetworkClient::write(buf, size); }
int NetworkClientSecure::available() { return NetworkClient::available(); }
int NetworkClientSecure::read() { return NetworkClient::read(); }
int NetworkClientSecure::read(uint8_t *buf, size_t size) { return NetworkClient::read(buf, size); }
uint8_t NetworkClientSecure::connected() { return NetworkClient::connected(); }

int NetworkClientSecure::lastError(char *buf, const size_t size)
{
    int erro = sslclient->last_error;
    mbedtls_strerror(erro, buf, size);
    return erro;
}

void NetworkClientSecure::setInsecure()
{
    _CA_cert = NULL;
    _cert = NULL;
    _private_key = NULL;
    _pskIdent = NULL;
    _psKey = NULL;
    _use_insecure = true;
}
void NetworkClientSecure::setCACert(const char *rootCA)
{
    if (_ca_cert_free && _CA_cert) {
        free((void *)_CA_cert);
        _ca_cert_free = false;
    }
    _CA_cert = rootCA;
    _use_insecure = false;
}
void NetworkClientSecure::setCACertBundle(const uint8_t *bundle, size_t size)
{
    _use_ca_bundle = bundle != NULL && size > 0;
    attach_ssl_certificate_bundle(sslclient.get(), _use_ca_bundle);
}
void NetworkClientSecure::useBuiltinCACertBundle()
{
    attach_ssl_certificate_bundle(sslclient.get(), true);
    _use_ca_bundle = true;
}
void NetworkClientSecure::setCertificate(const char *client_ca)
{
    if (_cert_free && _cert) {
        free((void *)_cert);
        _cert_free = false;
    }
    _cert = client_ca;
}
void NetworkClientSecure::setPrivateKey(const char *private_key)
{
    if (_private_key_free && _private_key) {
        free((void *)_private_key);
        _private_key_free = false;
    }
    _private_key = private_key;
}
void NetworkClientSecure::setPreSharedKey(const char *pskIdent, const char *psKey)
{
    _pskIdent = pskIdent;
    _psKey = psKey;
}
bool NetworkClientSecure::verify(const char *fp, const char *domain_name)
{
    return sslclient && verify_ssl_fingerprint(sslclient.get(), fp, domain_name);
}

char *NetworkClientSecure::_streamLoad(Stream &stream, size_t size)
{
    char *dest = (char *)malloc(size + 1);
    if (!dest) return nullptr;
    if (size != stream.readBytes(dest, size)) {
        free(dest);
        return nullptr;
    }
    dest[size] = '\0';
    return dest;
}
bool NetworkClientSecure::loadCACert(Stream &stream, size_t size)
{
    if (_CA_cert != NULL && _ca_cert_free) free(const_cast<char *>(_CA_cert));
    char *dest = _streamLoad(stream, size);
    if (!dest) return false;
    setCACert(dest);
    _ca_cert_free = true;
    return true;
}
bool NetworkClientSecure::loadCertificate(Stream &stream, size_t size)
{
    if (_cert != NULL && _cert_free) free(const_cast<char *>(_cert));
    char *dest = _streamLoad(stream, size);
    if (!dest) return false;
    setCertificate(dest);
    _cert_free = true;
    return true;
}
bool NetworkClientSecure::loadPrivateKey(Stream &stream, size_t size)
{
    if (_private_key != NULL && _private_key_free) free(const_cast<char *>(_private_key));
    char *dest = _streamLoad(stream, size);
    if (!dest) return false;
    setPrivateKey(dest);
    _private_key_free = true;
    return true;
}

void NetworkClientSecure::setHandshakeTimeout(unsigned long handshake_timeout) { sslclient->handshake_timeout = handshake_timeout * 1000; }
void NetworkClientSecure::setAlpnProtocols(const char **alpn_protos) { _alpn_protos = alpn_protos; }
void NetworkClientSecure::setCiphers(const int *ciphersuites) { _cipher_list = ciphersuites; }
int NetworkClientSecure::fd() const { return sslclient->socket; }
bool NetworkClientSecure::operator==(const NetworkClientSecure &rhs) { return sslclient == rhs.sslclient; }

#endif
