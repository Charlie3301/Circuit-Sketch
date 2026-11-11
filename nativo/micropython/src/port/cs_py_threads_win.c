/* cs_py_threads_win.c -- o que o coletor do MicroPython precisa do Windows para olhar a pilha das
 * outras threads (mpthreadport.c): a thread parada, os registradores dela e ate onde a pilha foi.
 * Fica separado: o windows.h nao convive com os cabecalhos do MicroPython. */
#include <windows.h>

/* Uma alca propria da thread que chama (a do GetCurrentThread so vale dentro dela). */
void *cs_py_thread_identidade(void)
{
    HANDLE h = NULL;
    DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(), &h, THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, 0);
    return h;
}

/* Onde a pilha da thread que chama comeca (o endereco mais alto). */
void *cs_py_thread_topo(void)
{
    return ((NT_TIB *)NtCurrentTeb())->StackBase;
}

/* Para a thread e devolve os registradores inteiros dela e o ponteiro da pilha. 0: nao deu. */
int cs_py_thread_parar(void *thread, void **registradores, int quantos, void **pilha)
{
    if (!thread || SuspendThread((HANDLE)thread) == (DWORD)-1) return 0;
    CONTEXT c;
    c.ContextFlags = CONTEXT_INTEGER | CONTEXT_CONTROL;
    if (!GetThreadContext((HANDLE)thread, &c)) return 0;
    DWORD64 valores[] = {c.Rax, c.Rbx, c.Rcx, c.Rdx, c.Rsi, c.Rdi, c.Rbp, c.R8, c.R9, c.R10, c.R11, c.R12, c.R13, c.R14, c.R15};
    int n = 0;
    for (; n < (int)(sizeof(valores) / sizeof(valores[0])) && n < quantos; n++) registradores[n] = (void *)valores[n];
    *pilha = (void *)c.Rsp;
    return n;
}

void cs_py_thread_seguir(void *thread)
{
    if (thread) ResumeThread((HANDLE)thread);
}

void cs_py_thread_soltar(void *thread)
{
    if (thread) CloseHandle((HANDLE)thread);
}
