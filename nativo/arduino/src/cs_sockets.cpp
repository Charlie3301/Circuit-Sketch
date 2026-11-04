/* cs_sockets.cpp -- os sockets do lwIP (sys/socket.h, lwip/sockets.h, netdb.h)
 * sobre a rede do simulador: os cabecalhos sao os de verdade do ESP-IDF, e
 * cada socket e um cliente ou servidor TCP (WiFiClient/WiFiServer) ou uma
 * porta UDP da bancada, como o resto da rede da placa. Com isso, os exemplos
 * do IDF (tcp_client, tcp_server, udp_server, http_request...) rodam, e o
 * programa fala com as outras placas e com o computador.
 *
 * As esperas sao as da placa: bloqueia ate chegar dado, ate o SO_RCVTIMEO, ou
 * responde EAGAIN com o socket em modo nao bloqueante. Cada espera passa a
 * vez (delay), entao as outras tarefas continuam rodando. */
#include "Arduino.h"
#if defined(ESP32)
#include "sdkconfig.h"
#include <errno.h>
#include <string.h>
#include <fcntl.h>
#include <io.h>
#include <stdarg.h>
#include "lwip/sockets.h"
#include "lwip/netdb.h"
#include "lwip/inet.h"
#include "WiFiClient.h"
#include "cs_objeto.h"
#include <vector>

namespace {

struct Socket {
    bool usado = false;
    int tipo = 0;
    int udp = -1;
    WiFiClient *cliente = nullptr;
    WiFiServer *servidor = nullptr;
    uint16_t porta = 0;
    int esperaDeLeitura = 0; /* em ms; 0 e para sempre, como no lwIP */
    bool naoBloqueia = false;
    bool fechouEscrita = false;
    bool temRemoto = false;
    sockaddr_in remoto = {};
    int erro = 0;
};

const int PRIMEIRO_FD = LWIP_SOCKET_OFFSET;
const int QUANTOS = CONFIG_LWIP_MAX_SOCKETS;
Socket socketsDaPlaca[CONFIG_LWIP_MAX_SOCKETS];

Socket *socketDe(int s)
{
    int i = s - PRIMEIRO_FD;
    if (i < 0 || i >= QUANTOS || !socketsDaPlaca[i].usado) {
        errno = EBADF;
        return nullptr;
    }
    return &socketsDaPlaca[i];
}

int novoSocket(int tipo)
{
    for (int i = 0; i < QUANTOS; i++) {
        if (!socketsDaPlaca[i].usado) {
            socketsDaPlaca[i] = Socket();
            socketsDaPlaca[i].usado = true;
            socketsDaPlaca[i].tipo = tipo;
            return PRIMEIRO_FD + i;
        }
    }
    errno = ENFILE;
    return -1;
}

String ipEmTexto(const sockaddr_in *a)
{
    char t[16];
    uint32_t ip = a->sin_addr.s_addr;
    snprintf(t, sizeof(t), "%u.%u.%u.%u", (unsigned)(ip & 255), (unsigned)(ip >> 8 & 255), (unsigned)(ip >> 16 & 255), (unsigned)(ip >> 24 & 255));
    return t;
}

void preencher(sockaddr_in *a, const String &ip, uint16_t porta)
{
    memset(a, 0, sizeof(*a));
    a->sin_len = sizeof(*a);
    a->sin_family = AF_INET;
    a->sin_port = lwip_htons(porta);
    IPAddress endereco;
    endereco.fromString(ip);
    a->sin_addr.s_addr = (uint32_t)endereco;
}

void devolver(const sockaddr_in &a, sockaddr *destino, socklen_t *tamanho)
{
    if (!destino || !tamanho) return;
    socklen_t n = *tamanho < sizeof(a) ? *tamanho : (socklen_t)sizeof(a);
    memcpy(destino, &a, n);
    *tamanho = sizeof(a);
}

bool abrirUdp(Socket *s, uint16_t porta)
{
    if (s->udp >= 0) return true;
    s->udp = (int)cs_obj_i(nullptr, "__udp_abrir", (int)porta);
    if (s->udp < 0) {
        errno = EADDRINUSE;
        return false;
    }
    s->porta = (uint16_t)cs_obj_i(nullptr, "__udp_porta", s->udp);
    return true;
}

/* Tem o que ler agora? (o servidor: uma conexao esperando; o TCP que fechou tambem "le": o fim) */
bool prontoParaLer(Socket *s)
{
    if (s->servidor) return s->servidor->hasClient();
    if (s->udp >= 0) return cs_obj_i(nullptr, "__udp_esperando", s->udp) > 0;
    if (s->cliente) return s->cliente->available() > 0 || !s->cliente->connected();
    return false;
}

bool prontoParaEscrever(Socket *s)
{
    if (s->tipo == SOCK_DGRAM) return true;
    return s->cliente && s->cliente->connected();
}

/* Espera dar para ler, respeitando o modo do socket. */
bool esperarLeitura(Socket *s, int flags)
{
    bool naoEspera = s->naoBloqueia || (flags & MSG_DONTWAIT);
    uint32_t inicio = millis();
    while (!prontoParaLer(s)) {
        if (naoEspera || (s->esperaDeLeitura > 0 && (int)(millis() - inicio) >= s->esperaDeLeitura)) {
            errno = EWOULDBLOCK;
            return false;
        }
        delay(1);
    }
    return true;
}

int msDe(const void *timeval_)
{
    const struct timeval *t = (const struct timeval *)timeval_;
    return (int)(t->tv_sec * 1000 + t->tv_usec / 1000);
}

} /* namespace */

extern "C" {

int lwip_socket(int domain, int type, int protocol)
{
    (void)protocol;
    if (domain != AF_INET && domain != AF_UNSPEC) {
        errno = EAFNOSUPPORT;
        return -1;
    }
    if (type != SOCK_STREAM && type != SOCK_DGRAM) {
        errno = EPROTONOSUPPORT;
        return -1;
    }
    return novoSocket(type);
}

int lwip_bind(int fd, const struct sockaddr *name, socklen_t namelen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (!name || namelen < sizeof(sockaddr_in)) {
        errno = EINVAL;
        return -1;
    }
    uint16_t porta = lwip_ntohs(((const sockaddr_in *)name)->sin_port);
    if (s->tipo == SOCK_DGRAM) return abrirUdp(s, porta) ? 0 : -1;
    s->porta = porta;
    return 0;
}

int lwip_listen(int fd, int backlog)
{
    (void)backlog;
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (s->tipo != SOCK_STREAM) {
        errno = EOPNOTSUPP;
        return -1;
    }
    if (!s->servidor) {
        s->servidor = new WiFiServer(s->porta);
        s->servidor->begin();
    }
    return 0;
}

int lwip_accept(int fd, struct sockaddr *addr, socklen_t *addrlen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (!s->servidor) {
        errno = EINVAL;
        return -1;
    }
    for (;;) {
        WiFiClient c = s->servidor->accept();
        if (c) {
            int novo = novoSocket(SOCK_STREAM);
            if (novo < 0) return -1;
            Socket *n = socketDe(novo);
            n->cliente = new WiFiClient(c);
            n->porta = s->porta;
            preencher(&n->remoto, c.remoteIP().toString(), c.remotePort());
            n->temRemoto = true;
            devolver(n->remoto, addr, addrlen);
            return novo;
        }
        if (s->naoBloqueia) {
            errno = EWOULDBLOCK;
            return -1;
        }
        delay(1);
    }
}

int lwip_connect(int fd, const struct sockaddr *name, socklen_t namelen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (!name || namelen < sizeof(sockaddr_in)) {
        errno = EINVAL;
        return -1;
    }
    const sockaddr_in *a = (const sockaddr_in *)name;
    s->remoto = *a;
    s->temRemoto = true;
    if (s->tipo == SOCK_DGRAM) return abrirUdp(s, 0) ? 0 : -1;
    if (!s->cliente) s->cliente = new WiFiClient();
    if (!s->cliente->connect(ipEmTexto(a).c_str(), lwip_ntohs(a->sin_port))) {
        s->erro = ECONNREFUSED;
        errno = ECONNREFUSED;
        return -1;
    }
    return 0;
}

ssize_t lwip_recvfrom(int fd, void *mem, size_t len, int flags, struct sockaddr *from, socklen_t *fromlen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (s->tipo == SOCK_DGRAM) {
        if (s->udp < 0 && !abrirUdp(s, 0)) return -1;
        if (!esperarLeitura(s, flags)) return -1;
        int n = (int)cs_obj_i(nullptr, "__udp_receber", s->udp, cs_vetor_mutavel((uint8_t *)mem, (int)len));
        if (n < 0) {
            errno = EWOULDBLOCK;
            return -1;
        }
        String quem = cs_obj_t(nullptr, "__udp_remetente", s->udp);
        int doisPontos = quem.indexOf(':');
        sockaddr_in a;
        preencher(&a, quem.substring(0, doisPontos), (uint16_t)quem.substring(doisPontos + 1).toInt());
        devolver(a, from, fromlen);
        return n;
    }
    if (!s->cliente) {
        errno = ENOTCONN;
        return -1;
    }
    if (!esperarLeitura(s, flags)) return -1;
    if (s->cliente->available() <= 0) return 0; /* o outro lado fechou */
    uint8_t *destino = (uint8_t *)mem;
    size_t n = 0;
    if (flags & MSG_PEEK) {
        int c = s->cliente->peek();
        if (c < 0) return 0;
        if (len) destino[n++] = (uint8_t)c;
    } else {
        int r = s->cliente->read(destino, len);
        n = r > 0 ? (size_t)r : 0;
    }
    if (s->temRemoto) devolver(s->remoto, from, fromlen);
    return (ssize_t)n;
}

ssize_t lwip_recv(int fd, void *mem, size_t len, int flags) { return lwip_recvfrom(fd, mem, len, flags, nullptr, nullptr); }
ssize_t lwip_read(int fd, void *mem, size_t len) { return lwip_recvfrom(fd, mem, len, 0, nullptr, nullptr); }

ssize_t lwip_readv(int fd, const struct iovec *iov, int iovcnt)
{
    ssize_t total = 0;
    for (int i = 0; i < iovcnt; i++) {
        ssize_t n = lwip_recvfrom(fd, iov[i].iov_base, iov[i].iov_len, i ? MSG_DONTWAIT : 0, nullptr, nullptr);
        if (n < 0) return total ? total : -1;
        total += n;
        if ((size_t)n < iov[i].iov_len) break;
    }
    return total;
}

ssize_t lwip_recvmsg(int fd, struct msghdr *message, int flags)
{
    if (!message || message->msg_iovlen < 1) {
        errno = EINVAL;
        return -1;
    }
    socklen_t nome = message->msg_namelen;
    ssize_t n = lwip_recvfrom(fd, message->msg_iov[0].iov_base, message->msg_iov[0].iov_len, flags, (struct sockaddr *)message->msg_name, &nome);
    message->msg_namelen = nome;
    message->msg_flags = 0;
    return n;
}

ssize_t lwip_sendto(int fd, const void *data, size_t size, int flags, const struct sockaddr *to, socklen_t tolen)
{
    (void)flags;
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (s->tipo == SOCK_DGRAM) {
        const sockaddr_in *a = to && tolen >= sizeof(sockaddr_in) ? (const sockaddr_in *)to : (s->temRemoto ? &s->remoto : nullptr);
        if (!a) {
            errno = EDESTADDRREQ;
            return -1;
        }
        if (!abrirUdp(s, 0)) return -1;
        if (cs_obj_i(nullptr, "__udp_enviar", s->udp, ipEmTexto(a), (int)lwip_ntohs(a->sin_port), cs_vetor((const uint8_t *)data, (int)size)) == 0) {
            errno = ENETUNREACH;
            return -1;
        }
        return (ssize_t)size;
    }
    if (!s->cliente || !s->cliente->connected() || s->fechouEscrita) {
        errno = s->cliente ? EPIPE : ENOTCONN;
        return -1;
    }
    return (ssize_t)s->cliente->write((const uint8_t *)data, size);
}

ssize_t lwip_send(int fd, const void *data, size_t size, int flags) { return lwip_sendto(fd, data, size, flags, nullptr, 0); }
ssize_t lwip_write(int fd, const void *data, size_t size) { return lwip_sendto(fd, data, size, 0, nullptr, 0); }

ssize_t lwip_writev(int fd, const struct iovec *iov, int iovcnt)
{
    ssize_t total = 0;
    for (int i = 0; i < iovcnt; i++) {
        ssize_t n = lwip_sendto(fd, iov[i].iov_base, iov[i].iov_len, 0, nullptr, 0);
        if (n < 0) return total ? total : -1;
        total += n;
    }
    return total;
}

ssize_t lwip_sendmsg(int fd, const struct msghdr *message, int flags)
{
    if (!message) {
        errno = EINVAL;
        return -1;
    }
    std::vector<uint8_t> tudo;
    for (int i = 0; i < message->msg_iovlen; i++) {
        const uint8_t *p = (const uint8_t *)message->msg_iov[i].iov_base;
        tudo.insert(tudo.end(), p, p + message->msg_iov[i].iov_len);
    }
    return lwip_sendto(fd, tudo.data(), tudo.size(), flags, (const struct sockaddr *)message->msg_name, message->msg_namelen);
}

int lwip_shutdown(int fd, int how)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (how == SHUT_WR || how == SHUT_RDWR) s->fechouEscrita = true;
    if (how == SHUT_RDWR && s->cliente) s->cliente->stop();
    return 0;
}

int lwip_close(int fd)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (s->cliente) {
        s->cliente->stop();
        delete s->cliente;
    }
    if (s->servidor) {
        s->servidor->close();
        delete s->servidor;
    }
    if (s->udp >= 0) cs_obj_n(nullptr, "__udp_fechar", s->udp);
    *s = Socket();
    return 0;
}

int lwip_getsockname(int fd, struct sockaddr *name, socklen_t *namelen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    String ip = s->cliente ? s->cliente->localIP().toString() : String("0.0.0.0");
    sockaddr_in a;
    preencher(&a, ip, s->cliente ? s->cliente->localPort() : s->porta);
    devolver(a, name, namelen);
    return 0;
}

int lwip_getpeername(int fd, struct sockaddr *name, socklen_t *namelen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (!s->temRemoto) {
        errno = ENOTCONN;
        return -1;
    }
    devolver(s->remoto, name, namelen);
    return 0;
}

int lwip_getsockopt(int fd, int level, int optname, void *optval, socklen_t *optlen)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (!optval || !optlen) {
        errno = EFAULT;
        return -1;
    }
    if (level == SOL_SOCKET && optname == SO_ERROR) {
        *(int *)optval = s->erro;
        s->erro = 0;
    } else if (level == SOL_SOCKET && optname == SO_TYPE) {
        *(int *)optval = s->tipo;
    } else if (level == SOL_SOCKET && (optname == SO_RCVTIMEO || optname == SO_SNDTIMEO)) {
        struct timeval *t = (struct timeval *)optval;
        t->tv_sec = s->esperaDeLeitura / 1000;
        t->tv_usec = (s->esperaDeLeitura % 1000) * 1000;
    } else {
        *(int *)optval = 0;
    }
    return 0;
}

int lwip_setsockopt(int fd, int level, int optname, const void *optval, socklen_t optlen)
{
    (void)optlen;
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (level == SOL_SOCKET && optname == SO_RCVTIMEO && optval) {
        s->esperaDeLeitura = msDe(optval);
    } else if (level == IPPROTO_IP && optname == IP_ADD_MEMBERSHIP && optval) {
        if (!abrirUdp(s, s->porta)) return -1;
        struct ip_mreq const *grupo = (struct ip_mreq const *)optval;
        sockaddr_in a = {};
        a.sin_addr.s_addr = grupo->imr_multiaddr.s_addr;
        cs_obj_n(nullptr, "__udp_grupo", s->udp, ipEmTexto(&a));
    }
    /* SO_REUSEADDR, TCP_NODELAY, SO_KEEPALIVE, SO_BROADCAST...: a rede da bancada ja se comporta assim. */
    return 0;
}

int lwip_fcntl(int fd, int cmd, int val)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (cmd == F_GETFL) return s->naoBloqueia ? O_NONBLOCK : 0;
    if (cmd == F_SETFL) {
        s->naoBloqueia = (val & O_NONBLOCK) != 0;
        return 0;
    }
    errno = ENOSYS;
    return -1;
}

int lwip_ioctl(int fd, long cmd, void *argp)
{
    Socket *s = socketDe(fd);
    if (!s) return -1;
    if (cmd == (long)FIONBIO) {
        s->naoBloqueia = argp && *(int *)argp != 0;
        return 0;
    }
    if (cmd == (long)FIONREAD) {
        *(int *)argp = s->udp >= 0 ? (int)cs_obj_i(nullptr, "__udp_esperando", s->udp) : s->cliente ? s->cliente->available() : 0;
        return 0;
    }
    errno = ENOSYS;
    return -1;
}

int lwip_select(int maxfdp1, fd_set *readset, fd_set *writeset, fd_set *exceptset, struct timeval *timeout)
{
    int limite = timeout ? msDe(timeout) : -1;
    uint32_t inicio = millis();
    fd_set entrada, saida;
    FD_ZERO(&entrada);
    FD_ZERO(&saida);
    for (;;) {
        int prontos = 0;
        for (int fd = PRIMEIRO_FD; fd < maxfdp1 && fd < PRIMEIRO_FD + QUANTOS; fd++) {
            Socket *s = &socketsDaPlaca[fd - PRIMEIRO_FD];
            if (!s->usado) continue;
            if (readset && FD_ISSET(fd, readset) && prontoParaLer(s)) {
                FD_SET(fd, &entrada);
                prontos++;
            }
            if (writeset && FD_ISSET(fd, writeset) && prontoParaEscrever(s)) {
                FD_SET(fd, &saida);
                prontos++;
            }
        }
        if (prontos || (limite >= 0 && (int)(millis() - inicio) >= limite)) {
            if (readset) *readset = entrada;
            if (writeset) *writeset = saida;
            if (exceptset) FD_ZERO(exceptset);
            return prontos;
        }
        delay(1);
    }
}

int lwip_poll(struct pollfd *fds, nfds_t nfds, int timeout)
{
    uint32_t inicio = millis();
    for (;;) {
        int prontos = 0;
        for (nfds_t i = 0; i < nfds; i++) {
            fds[i].revents = 0;
            int j = fds[i].fd - PRIMEIRO_FD;
            if (j < 0 || j >= QUANTOS || !socketsDaPlaca[j].usado) {
                if (fds[i].fd >= 0) fds[i].revents = POLLNVAL;
            } else {
                if ((fds[i].events & POLLIN) && prontoParaLer(&socketsDaPlaca[j])) fds[i].revents |= POLLIN;
                if ((fds[i].events & POLLOUT) && prontoParaEscrever(&socketsDaPlaca[j])) fds[i].revents |= POLLOUT;
            }
            if (fds[i].revents) prontos++;
        }
        if (prontos || (timeout >= 0 && (int)(millis() - inicio) >= timeout)) return prontos;
        delay(1);
    }
}

/* ---- o POSIX do socket: na placa a VFS do IDF manda estes para o lwIP; aqui,
 * o numero do socket vai para o lwIP e o resto continua nos do Windows ---- */
static bool ehSocket(int fd) { return fd >= PRIMEIRO_FD && fd < PRIMEIRO_FD + QUANTOS; }

int close(int fd) { return ehSocket(fd) ? lwip_close(fd) : _close(fd); }
int read(int fd, void *buf, unsigned int n) { return ehSocket(fd) ? (int)lwip_read(fd, buf, n) : _read(fd, buf, n); }
int write(int fd, const void *buf, unsigned int n) { return ehSocket(fd) ? (int)lwip_write(fd, buf, n) : _write(fd, buf, n); }

int select(int nfds, fd_set *readfds, fd_set *writefds, fd_set *errorfds, struct timeval *timeout)
{
    return lwip_select(nfds, readfds, writefds, errorfds, timeout);
}
int poll(struct pollfd *fds, nfds_t nfds, int timeout) { return lwip_poll(fds, nfds, timeout); }

int fcntl(int fd, int cmd, ...)
{
    va_list args;
    va_start(args, cmd);
    int val = va_arg(args, int);
    va_end(args);
    return lwip_fcntl(fd, cmd, val);
}

int ioctl(int fd, int request, ...)
{
    va_list args;
    va_start(args, request);
    void *arg = va_arg(args, void *);
    va_end(args);
    return lwip_ioctl(fd, request, arg);
}

/* O IPv6 com "%interface": as interfaces do lwIP nao existem aqui (os sockets sao os da bancada). */
struct netif *netif_find(const char *name)
{
    (void)name;
    return nullptr;
}

/* ---- os enderecos: o ip4addr_* e o ip6addr_* sao os do lwIP (arduino-esp32/idf) ---- */
const ip_addr_t ip_addr_any_type = IPADDR_ANY_TYPE_INIT;

char *ipaddr_ntoa_r(const ip_addr_t *addr, char *buf, int buflen)
{
    if (!addr) return nullptr;
    if (IP_IS_V6(addr)) return ip6addr_ntoa_r(ip_2_ip6(addr), buf, buflen);
    return ip4addr_ntoa_r(ip_2_ip4(addr), buf, buflen);
}

char *ipaddr_ntoa(const ip_addr_t *addr)
{
    static char texto[IP6ADDR_STRLEN_MAX];
    return ipaddr_ntoa_r(addr, texto, sizeof(texto));
}

int ipaddr_aton(const char *cp, ip_addr_t *addr)
{
    if (!cp) return 0;
    if (strchr(cp, ':')) {
        if (addr) IP_SET_TYPE(addr, IPADDR_TYPE_V6);
        return ip6addr_aton(cp, addr ? ip_2_ip6(addr) : nullptr);
    }
    if (addr) IP_SET_TYPE(addr, IPADDR_TYPE_V4);
    return ip4addr_aton(cp, addr ? ip_2_ip4(addr) : nullptr);
}

const char *lwip_inet_ntop(int af, const void *src, char *dst, socklen_t size)
{
    const char *r = nullptr;
    if (af == AF_INET) r = ip4addr_ntoa_r((const ip4_addr_t *)src, dst, (int)size);
    else if (af == AF_INET6) r = ip6addr_ntoa_r((const ip6_addr_t *)src, dst, (int)size);
    else errno = EAFNOSUPPORT;
    if (!r && af != 0 && errno != EAFNOSUPPORT) errno = ENOSPC;
    return r;
}

int lwip_inet_pton(int af, const char *src, void *dst)
{
    if (af == AF_INET) return ip4addr_aton(src, (ip4_addr_t *)dst);
    if (af == AF_INET6) return ip6addr_aton(src, (ip6_addr_t *)dst);
    errno = EAFNOSUPPORT;
    return -1;
}

/* ---- os nomes (netdb.h): o DNS da bancada ---- */
struct hostent *lwip_gethostbyname(const char *name)
{
    static struct hostent resposta;
    static char nome[64];
    static ip_addr_t endereco;
    static ip_addr_t *lista[2];
    static char *apelidos[1];
    String ip = cs_obj_t(nullptr, "__dns", name ? name : "");
    if (!ip.length()) return nullptr;
    IPAddress a;
    a.fromString(ip);
    memset(&endereco, 0, sizeof(endereco));
    ip_addr_set_ip4_u32(&endereco, (uint32_t)a);
    snprintf(nome, sizeof(nome), "%s", name);
    lista[0] = &endereco;
    lista[1] = nullptr;
    apelidos[0] = nullptr;
    resposta.h_name = nome;
    resposta.h_aliases = apelidos;
    resposta.h_addrtype = AF_INET;
    resposta.h_length = sizeof(ip4_addr_t);
    resposta.h_addr_list = (char **)lista;
    return &resposta;
}

int lwip_gethostbyname_r(const char *name, struct hostent *ret, char *buf, size_t buflen, struct hostent **result, int *h_errnop)
{
    (void)buf;
    (void)buflen;
    struct hostent *h = lwip_gethostbyname(name);
    if (result) *result = h;
    if (!h) {
        if (h_errnop) *h_errnop = HOST_NOT_FOUND;
        return -1;
    }
    *ret = *h;
    return 0;
}

int lwip_getaddrinfo(const char *nodename, const char *servname, const struct addrinfo *hints, struct addrinfo **res)
{
    if (!res || (!nodename && !servname)) return EAI_FAIL;
    if (hints && hints->ai_family != AF_INET && hints->ai_family != AF_UNSPEC) return EAI_FAMILY;
    String ip = nodename ? cs_obj_t(nullptr, "__dns", nodename) : String("0.0.0.0");
    if (!ip.length()) return HOST_NOT_FOUND;
    /* O addrinfo e o endereco juntos, como o lwIP aloca: o freeaddrinfo solta tudo de uma vez. */
    size_t tamanho = sizeof(struct addrinfo) + sizeof(sockaddr_in);
    struct addrinfo *ai = (struct addrinfo *)calloc(1, tamanho);
    if (!ai) return EAI_MEMORY;
    sockaddr_in *a = (sockaddr_in *)(ai + 1);
    preencher(a, ip, servname ? (uint16_t)atoi(servname) : 0);
    ai->ai_family = AF_INET;
    ai->ai_socktype = hints ? hints->ai_socktype : 0;
    ai->ai_protocol = hints ? hints->ai_protocol : 0;
    ai->ai_addrlen = sizeof(sockaddr_in);
    ai->ai_addr = (struct sockaddr *)a;
    *res = ai;
    return 0;
}

void lwip_freeaddrinfo(struct addrinfo *ai) { free(ai); }

const char *gai_strerror(int ecode)
{
    switch (ecode) {
    case 0: return "Success";
    case EAI_FAIL: return "Failed";
    case EAI_FAMILY: return "Unsupported address family";
    case EAI_MEMORY: return "Out of memory";
    case HOST_NOT_FOUND: return "Host not found";
    default: return "Unknown error";
    }
}

} /* extern "C" */

#endif
