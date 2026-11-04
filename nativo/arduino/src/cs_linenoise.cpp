/* cs_linenoise.cpp -- a API do linenoise (o editor de linha do Console e do
 * esp_console) sobre o monitor serial: le pela funcao que o Console.cpp
 * instala (a Serial), ecoa o que se digita, apaga com o backspace, completa com
 * o TAB e guarda o historico. O linenoise de verdade fala com um terminal
 * VT100 pelo stdin/stdout; o monitor do simulador manda a linha pronta, entao
 * ele sempre fica no modo "burro" dele. */
#include "Arduino.h"
#if defined(ESP32)
#include "linenoise/linenoise.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <string>
#include <deque>

static linenoiseCompletionCallback *cs_completar = nullptr;
static linenoiseHintsCallback *cs_dicas = nullptr;
static linenoiseFreeHintsCallback *cs_soltar_dica = nullptr;
static linenoise_read_bytes_fn cs_ler = nullptr;
static std::deque<std::string> cs_historico;
static size_t cs_max_historico = 100, cs_max_linha = 4096;
static bool cs_aceita_vazio = true;

extern "C" {

void linenoiseSetCompletionCallback(linenoiseCompletionCallback *fn) { cs_completar = fn; }
void linenoiseSetHintsCallback(linenoiseHintsCallback *fn) { cs_dicas = fn; }
void linenoiseSetFreeHintsCallback(linenoiseFreeHintsCallback *fn) { cs_soltar_dica = fn; }
void linenoiseAddCompletion(linenoiseCompletions *lc, const char *str)
{
    char **novas = (char **)realloc(lc->cvec, sizeof(char *) * (lc->len + 1));
    char *copia = strdup(str);
    if (!novas || !copia) {
        free(copia);
        if (novas) lc->cvec = novas;
        return;
    }
    lc->cvec = novas;
    lc->cvec[lc->len++] = copia;
}

/* O monitor nao e um terminal VT100: o sondador responde "nao", como o linenoise num terminal burro. */
int linenoiseProbe(void) { return -2; }
void linenoiseSetMultiLine(int ml) { (void)ml; }
void linenoiseSetDumbMode(int set) { (void)set; }
bool linenoiseIsDumbMode(void) { return true; }
void linenoiseClearScreen(void) {}
void linenoisePrintKeyCodes(void) {}
void linenoiseAllowEmpty(bool permitir) { cs_aceita_vazio = permitir; }
int linenoiseSetMaxLineLen(size_t len)
{
    if (len < 4) return -1;
    cs_max_linha = len;
    return 0;
}
void linenoiseSetReadFunction(linenoise_read_bytes_fn read_fn) { cs_ler = read_fn; }
void linenoiseSetReadCharacteristics(void) {}

void linenoiseFree(void *ptr) { free(ptr); }

int linenoiseHistoryAdd(const char *line)
{
    if (!line || cs_max_historico == 0) return 0;
    if (!cs_historico.empty() && cs_historico.back() == line) return 0;
    cs_historico.push_back(line);
    while (cs_historico.size() > cs_max_historico) cs_historico.pop_front();
    return 1;
}
int linenoiseHistorySetMaxLen(int len)
{
    if (len < 1) return 0;
    cs_max_historico = (size_t)len;
    while (cs_historico.size() > cs_max_historico) cs_historico.pop_front();
    return 1;
}
int linenoiseHistorySave(const char *filename)
{
    FILE *f = fopen(filename, "w");
    if (!f) return -1;
    for (const std::string &l : cs_historico) fprintf(f, "%s\n", l.c_str());
    fclose(f);
    return 0;
}
int linenoiseHistoryLoad(const char *filename)
{
    FILE *f = fopen(filename, "r");
    if (!f) return -1;
    char linha[512];
    while (fgets(linha, sizeof(linha), f)) {
        linha[strcspn(linha, "\r\n")] = 0;
        linenoiseHistoryAdd(linha);
    }
    fclose(f);
    return 0;
}
void linenoiseHistoryFree(void) { cs_historico.clear(); }

char *linenoise(const char *prompt)
{
    if (prompt) printf("%s", prompt);
    std::string linha;
    for (;;) {
        char c = 0;
        long n;
        if (cs_ler) {
            n = (long)cs_ler(0, &c, 1);
            if (n < 0) return nullptr;
        } else {
            n = Serial.available() ? (c = (char)Serial.read(), 1) : 0;
        }
        if (n == 0) {
            delay(5);
            continue;
        }
        if (c == '\r' || c == '\n') {
            /* O monitor manda "linha\n" (ou "\r\n"): a linha vazia do CRLF nao conta. */
            if (linha.empty() && !cs_aceita_vazio) continue;
            printf("\n");
            return strdup(linha.c_str());
        }
        if (c == 8 || c == 127) {
            if (!linha.empty()) {
                linha.pop_back();
                printf("\b \b");
            }
            continue;
        }
        if (c == '\t' && cs_completar) {
            linenoiseCompletions lc = {0, nullptr};
            cs_completar(linha.c_str(), &lc);
            if (lc.len >= 1) {
                linha = lc.cvec[0];
                printf("\r%s%s", prompt ? prompt : "", linha.c_str());
            }
            for (size_t i = 0; i < lc.len; i++) free(lc.cvec[i]);
            free(lc.cvec);
            continue;
        }
        if ((unsigned char)c < 32) continue;
        if (linha.size() + 1 >= cs_max_linha) continue;
        linha.push_back(c);
        putchar(c);
    }
}

} /* extern "C" */
#endif
