/* cs_ble.cpp -- o objeto de callbacks do simulador por tras do objeto de
 * callbacks do sketch (ver cs_ble.h): o simulador chama o metodo pelo nome, e
 * o despachante da classe chama o metodo virtual do sketch. */
#include "cs_ble.h"

static void chamarDespachante(void *fn, void *cb, int n, const cs_val *a)
{
    if (n < 1 || !a[0].s) return;
    String metodo(a[0].s, (unsigned)a[0].n);
    ((cs_despachar_t)fn)(cb, metodo.c_str(), n - 1, a + 1);
}

const char *cs_callbacks_do_cpp(cs_despachar_t despachar, void *cb)
{
    static std::map<void *, String> feitos;
    auto achado = feitos.find(cb);
    if (achado != feitos.end()) return achado->second.c_str();
    int indice = cs_registrar_callback((void *)despachar, &chamarDespachante, cb);
    cs_val v[] = {cs_i(indice)};
    return (feitos[cb] = cs_texto(nullptr, "__callbacks", 1, v)).c_str();
}
