/* assert que falha faz o que a ESP32 faz: "assert failed" no serial e
 * panico da placa. */
#undef assert
#ifdef NDEBUG
#define assert(x) ((void)0)
#else
void __cs_assert(const char *expressao, const char *arquivo, int linha, const char *funcao);
#define assert(x) ((x) ? (void)0 : __cs_assert(#x, __FILE__, __LINE__, __func__))
#endif

#ifndef static_assert
#define static_assert _Static_assert
#endif
