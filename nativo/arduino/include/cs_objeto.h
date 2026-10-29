/* cs_objeto.h -- as bibliotecas que o simulador imita, vistas do C++.
 *
 * O servo que gira na bancada, o LCD que mostra o texto, a fita de NeoPixel:
 * esses aparelhos ja existem no simulador, e o interpretador os comanda pela
 * API da biblioteca (lcd.print, servo.write). Aqui cada biblioteca e uma
 * classe C++ com a API de verdade (a que o sketch e as outras bibliotecas
 * compilam contra), e cada objeto dela e um objeto do simulador: o construtor
 * cria o objeto la ("Servo direcao;"), e cada metodo vira a mesma chamada.
 *
 * Os argumentos convertem sozinhos (cs_de): numero, texto, String, o &Wire
 * do construtor (pelo nome), a funcao de callback (registrada, pelo nome) e
 * o vetor de bytes (cs_bytes, que volta escrito quando o simulador muda). */
#ifndef CS_OBJETO_H
#define CS_OBJETO_H

#include "Arduino.h"
#include <type_traits>
#include <utility>
#include <functional>
#include <map>
#include <string>

class TwoWire;
class SPIClass;

enum { CS_V_BYTES = 7, CS_V_BYTES_MUDA = 8 };

/* Um vetor de bytes como argumento: o simulador ve um vetor (como o sketch
 * interpretado veria); com "muda", o que ele escrever volta para ca. */
struct cs_bytes {
    const void *p;
    int n;
    bool muda;
};
inline cs_bytes cs_vetor(const void *p, int n) { return {p, n, false}; }
inline cs_bytes cs_vetor_mutavel(void *p, int n) { return {p, n, true}; }

/* O objeto do core passado por endereco ("&Wire", "&Serial"): pelo nome. */
struct cs_nome_de {
    const char *nome;
};

int cs_registrar_rotina(void (*f)(void), void (*fa)(void *), void *arg);

inline cs_val cs_de(cs_val v) { return v; }
inline cs_val cs_de(bool v) { return cs_b(v); }
inline cs_val cs_de(char v) { return cs_c((unsigned char)v); }
inline cs_val cs_de(signed char v) { return cs_i(v); }
inline cs_val cs_de(unsigned char v) { return cs_i(v); }
inline cs_val cs_de(short v) { return cs_i(v); }
inline cs_val cs_de(unsigned short v) { return cs_i(v); }
inline cs_val cs_de(int v) { return cs_i(v); }
inline cs_val cs_de(unsigned int v) { return cs_i(v); }
inline cs_val cs_de(long v) { return cs_i(v); }
inline cs_val cs_de(unsigned long v) { return cs_i((int64_t)v); }
inline cs_val cs_de(long long v) { return cs_i(v); }
inline cs_val cs_de(unsigned long long v) { return cs_i((int64_t)v); }
inline cs_val cs_de(float v) { return cs_r(v); }
inline cs_val cs_de(double v) { return cs_r(v); }
inline cs_val cs_de(const char *v) { return cs_t(v); }
inline cs_val cs_de(char *v) { return cs_t(v); }
inline cs_val cs_de(const String &v) { return cs_tn(v.c_str(), (int)v.length()); }
inline cs_val cs_de(const __FlashStringHelper *v) { return cs_t(reinterpret_cast<const char *>(v)); }
inline cs_val cs_de(cs_nome_de v) { return cs_nome(v.nome); }
inline cs_val cs_de(cs_bytes v)
{
    cs_val r = {v.muda ? CS_V_BYTES_MUDA : CS_V_BYTES, v.n, 0, 0, (const char *)v.p};
    return r;
}
cs_val cs_de(TwoWire *w);
cs_val cs_de(TwoWire &w);
cs_val cs_de(SPIClass *s);
cs_val cs_de(SPIClass &s);
cs_val cs_de(HardwareSerial *s);
cs_val cs_de(HardwareSerial &s);
cs_val cs_de(Stream *s);

class CsObjeto;

/* O ponteiro unico de cada objeto que o simulador devolve pelo nome (o
 * servidor BLE que volta de dois lugares e um so). */
template <class T> T *cs_envolver(const String &nome)
{
    if (!nome.length()) return nullptr;
    static std::map<std::string, T *> vistos;
    auto &p = vistos[nome.c_str()];
    if (!p) p = new T(nome);
    return p;
}

/* ---- os callbacks: a funcao do sketch vira um nome que o simulador chama ----
 * O argumento que chega (numero, texto, vetor de bytes) vira o tipo do
 * parametro da funcao: char* topic, byte* payload, unsigned int length... */
template <class T> struct cs_para {
    static T de(const cs_val &v)
    {
        if constexpr (std::is_same<T, String>::value)
            return v.tipo == CS_V_TEXTO || v.tipo == CS_V_BYTES ? String(v.s ? v.s : "", (unsigned)v.n) : String((long)v.i);
        else if constexpr (std::is_pointer<T>::value && std::is_base_of<CsObjeto, std::remove_cv_t<std::remove_pointer_t<T>>>::value)
            return cs_envolver<std::remove_cv_t<std::remove_pointer_t<T>>>(String(v.s ? v.s : "", (unsigned)v.n));
        else if constexpr (std::is_pointer<T>::value)
            return (T)(void *)v.s;
        else if constexpr (std::is_floating_point<T>::value)
            return (T)(v.tipo == CS_V_REAL ? v.d : (double)v.i);
        else if constexpr (std::is_arithmetic<T>::value || std::is_enum<T>::value)
            return (T)(v.tipo == CS_V_REAL ? (int64_t)v.d : v.i);
        else
            return T{};
    }
};
template <class T> struct cs_para<const T &> : cs_para<T> {};
template <class T> struct cs_para<T &> : cs_para<T> {};
template <class T> struct cs_para<const T> : cs_para<T> {};

template <class R, class... P> struct CsChamador {
    template <size_t... I> static void chamar_(void *fn, const cs_val *a, std::index_sequence<I...>)
    {
        ((R(*)(P...))fn)(cs_para<P>::de(a[I])...);
    }
    static void chamar(void *fn, void *, int n, const cs_val *a)
    {
        cs_val vazios[sizeof...(P) + 1] = {};
        for (int i = 0; i < n && i < (int)sizeof...(P); i++) vazios[i] = a[i];
        chamar_(fn, vazios, std::index_sequence_for<P...>{});
    }
};

cs_val cs_nome_da_rotina(int indice);

template <class R, class... P> inline cs_val cs_de(R (*f)(P...))
{
    return cs_nome_da_rotina(cs_registrar_callback((void *)f, &CsChamador<R, P...>::chamar, nullptr));
}

/* O std::function (a lambda que captura, o callback do core 3.x da ESP32):
 * uma copia guardada para sempre, chamada pelo mesmo desempacotador. */
template <class R, class... P> struct CsChamadorDeFuncao {
    template <size_t... I> static void chamar_(void *arg, const cs_val *a, std::index_sequence<I...>)
    {
        (*(std::function<R(P...)> *)arg)(cs_para<P>::de(a[I])...);
    }
    static void chamar(void *, void *arg, int n, const cs_val *a)
    {
        cs_val vazios[sizeof...(P) + 1] = {};
        for (int i = 0; i < n && i < (int)sizeof...(P); i++) vazios[i] = a[i];
        chamar_(arg, vazios, std::index_sequence_for<P...>{});
    }
};

template <class R, class... P> inline cs_val cs_de(std::function<R(P...)> f)
{
    auto *guardada = new std::function<R(P...)>(std::move(f));
    return cs_nome_da_rotina(cs_registrar_callback((void *)guardada, &CsChamadorDeFuncao<R, P...>::chamar, guardada));
}

/* A lambda sem captura ("server.on("/", []() {...})"): a funcao dela. */
template <class F, class = typename std::enable_if<std::is_class<F>::value>::type, class = decltype(+std::declval<F>())>
inline cs_val cs_de(F f)
{
    return cs_de(+f);
}

/* Os enums da biblioteca entram como numero. */
template <class E, class = typename std::enable_if<std::is_enum<E>::value>::type>
inline cs_val cs_de(E e) { return cs_i((int64_t)e); }

String cs_texto(const char *alvo, const char *nome, int n, const cs_val *args);

template <class... A> inline int64_t cs_obj_i(const char *alvo, const char *m, A... a)
{
    cs_val v[] = {cs_de(a)..., cs_val{}};
    return cs_int(alvo, m, (int)sizeof...(A), v);
}
template <class... A> inline double cs_obj_r(const char *alvo, const char *m, A... a)
{
    cs_val v[] = {cs_de(a)..., cs_val{}};
    return cs_real(alvo, m, (int)sizeof...(A), v);
}
template <class... A> inline void cs_obj_n(const char *alvo, const char *m, A... a)
{
    cs_val v[] = {cs_de(a)..., cs_val{}};
    cs_nada(alvo, m, (int)sizeof...(A), v);
}
template <class... A> inline String cs_obj_t(const char *alvo, const char *m, A... a)
{
    cs_val v[] = {cs_de(a)..., cs_val{}};
    return cs_texto(alvo, m, (int)sizeof...(A), v);
}

/* O pedaco e texto UTF-8 valido (o acento do sketch e; um byte de dado nao). */
inline bool cs_texto_utf8(const uint8_t *p, size_t n)
{
    size_t i = 0;
    while (i < n) {
        uint8_t c = p[i];
        int seguem = c < 0x80 ? 0 : (c & 0xE0) == 0xC0 ? 1 : (c & 0xF0) == 0xE0 ? 2 : (c & 0xF8) == 0xF0 ? 3 : -1;
        if (seguem < 0 || c == 0 || (seguem > 0 && i + (size_t)seguem >= n)) return false;
        for (int k = 1; k <= seguem; k++)
            if ((p[i + (size_t)k] & 0xC0) != 0x80) return false;
        i += (size_t)seguem + 1;
    }
    return true;
}

/* O objeto do simulador por tras de cada objeto C++. */
class CsObjeto {
protected:
    char cs_nome[32];

    CsObjeto() { cs_nome[0] = 0; }

    template <class... A> void cs_criar(const char *classe, A... a)
    {
        cs_val v[] = {cs_t(classe), cs_de(a)..., cs_val{}};
        String nome = cs_texto(nullptr, "__criar", (int)sizeof...(A) + 1, v);
        snprintf(cs_nome, sizeof(cs_nome), "%s", nome.c_str());
    }

    /* "Objeto" que o simulador devolveu (o DateTime do RTC.now()). */
    void cs_adotar(const String &nome) { snprintf(cs_nome, sizeof(cs_nome), "%s", nome.c_str()); }

public:
    const char *nomeNoSimulador() const { return cs_nome; }
    CsObjeto(const CsObjeto &) = default;
    CsObjeto &operator=(const CsObjeto &) = default;
};

/* Outro objeto de biblioteca como argumento ("irrecv.decode(&results)"): pelo nome. */
inline cs_val cs_de(const CsObjeto *o) { return cs_nome(o->nomeNoSimulador()); }
inline cs_val cs_de(const CsObjeto &o) { return cs_nome(o.nomeNoSimulador()); }

/* Os metodos, pelo tipo da volta. Os argumentos sao livres (template): a
 * conversao e a do cs_de, e o padrao de cada um e o do simulador. */
#define CS_M_INT(tipo, metodo)                                                          \
    template <class... A> tipo metodo(A... a) { return (tipo)cs_obj_i(cs_nome, #metodo, a...); }
#define CS_M_REAL(tipo, metodo)                                                         \
    template <class... A> tipo metodo(A... a) { return (tipo)cs_obj_r(cs_nome, #metodo, a...); }
#define CS_M_BOOL(metodo)                                                               \
    template <class... A> bool metodo(A... a) { return cs_obj_i(cs_nome, #metodo, a...) != 0; }
#define CS_M_NADA(metodo)                                                               \
    template <class... A> void metodo(A... a) { cs_obj_n(cs_nome, #metodo, a...); }
#define CS_M_TEXTO(metodo)                                                              \
    template <class... A> String metodo(A... a) { return cs_obj_t(cs_nome, #metodo, a...); }

/* A escrita de texto pelo Print (o formato do numero e o do core), num print so. */
#define CS_PRINT_PELO_SIMULADOR                                                         \
    size_t write(uint8_t c) override                                                    \
    {                                                                                   \
        char t[2] = {(char)c, 0};                                                       \
        cs_obj_n(cs_nome, "print", cs_tn(t, 1));                                        \
        return 1;                                                                       \
    }                                                                                   \
    size_t write(const uint8_t *b, size_t n) override                                   \
    {                                                                                   \
        cs_obj_n(cs_nome, "print", cs_tn((const char *)b, (int)n));                     \
        return n;                                                                       \
    }                                                                                   \
    using Print::write;

#endif
