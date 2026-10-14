/* cs_host.h -- a ponte entre o C nativo e o simulador.
 *
 * O codigo C do projeto (o DOOM, por exemplo) roda nativo no PC,
 * compilado pelo TinyCC. Tudo o que ele faria no hardware passa por
 * aqui: os pinos e o SPI chegam no circuito, os arquivos no cartao SD
 * simulado, o relogio e o da placa, o WiFi e a rede da bancada.
 *
 * A ordem dos campos e contrato com o lado C# (motor/Nativo/Host.cs):
 * mudou aqui, muda la, na mesma posicao.
 *
 * Regra de ouro: nenhuma funcao daqui salta (longjmp) nem lanca nada.
 * Quando a placa esta parando, as que esperam devolvem um codigo que diz
 * isso, e e o lado C que encerra a tarefa -- sempre por um salto que so
 * atravessa quadros de C. */
#ifndef CS_HOST_H
#define CS_HOST_H

#include <stdint.h>

struct cs_tarefa;
struct cs_pedido_http;

typedef struct cs_host {
    /* ---- texto ---- */
    void    (*escrever)(const char *texto, int n);                 /* UART0: o monitor serial */
    int     (*ler_serial)(void);                                   /* um byte, ou -1 */
    int     (*serial_disponivel)(void);

    /* ---- tempo ---- */
    int64_t (*micros)(void);
    int     (*dormir)(int64_t us);                                 /* 0 ok, -1 = a tarefa tem que parar */
    void    (*ceder)(void);

    /* ---- tarefas ---- */
    int     (*tarefa_criar)(struct cs_tarefa *t, int pilha);       /* 1 criou */
    void    (*tarefa_acordar)(struct cs_tarefa *t);                /* sai de qualquer espera (vTaskDelete) */

    /* ---- semaforos, filas e grupos de eventos ---- */
    int64_t (*sem_criar)(int tipo, int maximo, int inicial);       /* CS_SEM_* */
    int     (*sem_pegar)(int64_t s, int64_t espera_us);            /* 1 pegou, 0 tempo, -1 parar */
    int     (*sem_dar)(int64_t s);                                 /* 1 deu, 0 cheio ou nao e o dono */
    int     (*sem_contagem)(int64_t s);
    struct cs_tarefa *(*sem_dono)(int64_t s);
    void    (*sem_apagar)(int64_t s);
    int64_t (*fila_criar)(int tamanho, int item);
    int     (*fila_enviar)(int64_t f, const void *item, int64_t espera_us, int modo);  /* CS_FILA_* */
    int     (*fila_receber)(int64_t f, void *item, int64_t espera_us, int espiar);
    int     (*fila_contagem)(int64_t f);
    int     (*fila_livre)(int64_t f);
    void    (*fila_limpar)(int64_t f);
    void    (*fila_apagar)(int64_t f);
    int64_t (*grupo_criar)(void);
    uint32_t (*grupo_mudar)(int64_t g, uint32_t ligar, uint32_t desligar);
    uint32_t (*grupo_esperar)(int64_t g, uint32_t bits, int limpar, int todos, int64_t espera_us, int *parar);
    void    (*grupo_apagar)(int64_t g);

    /* ---- pinos ---- */
    void    (*gpio_modo)(int pino, int modo);                      /* CS_GPIO_* */
    void    (*gpio_escrever)(int pino, int nivel);
    int     (*gpio_ler)(int pino);
    void    (*gpio_interrupcao)(int pino, int tipo);               /* CS_BORDA_*; 0 desliga */
    int     (*gpio_proxima_borda)(int64_t espera_us);              /* pino, -1 tempo, -2 parar */

    /* ---- SPI ---- */
    int     (*spi_barramento)(int host, int mosi, int miso, int sclk);   /* esp_err_t */
    int     (*spi_liberar)(int host);
    int64_t (*spi_dispositivo)(int host, int cs, int hz, int modo, int flags);
    void    (*spi_remover)(int64_t dispositivo);
    int     (*spi_transferir)(int64_t dispositivo, const uint8_t *tx, uint8_t *rx, int bits_tx, int bits_rx);

    /* ---- arquivos: cartao SD e flash ---- */
    int     (*montar)(const char *ponto, int tipo, int host, int cs, const char *particao);  /* esp_err_t */
    int     (*desmontar)(const char *ponto);
    int64_t (*arq_abrir)(const char *caminho, int modo);           /* CS_ARQ_*; negativo = -errno */
    int     (*arq_ler)(int64_t a, void *buf, int n);
    int     (*arq_escrever)(int64_t a, const void *buf, int n);
    int64_t (*arq_posicionar)(int64_t a, int64_t pos, int de_onde);
    int64_t (*arq_tamanho)(int64_t a);
    void    (*arq_fechar)(int64_t a);
    int     (*arq_info)(const char *caminho, int64_t *tamanho);    /* 0 nao existe, 1 arquivo, 2 pasta */
    int     (*arq_apagar)(const char *caminho);                    /* 0 ok, -errno */
    int     (*arq_renomear)(const char *velho, const char *novo);
    int     (*arq_criar_pasta)(const char *caminho);
    int64_t (*pasta_abrir)(const char *caminho);
    int     (*pasta_ler)(int64_t p, char *nome, int n, int *tipo); /* 1 leu, 0 acabou */
    void    (*pasta_fechar)(int64_t p);
    int64_t (*particao_achar)(int tipo, int subtipo, const char *rotulo, int indice,
                              int64_t *endereco, int64_t *tamanho, char *rotulo_achado,
                              int *tipo_achado, int *subtipo_achado);
    int     (*particao_ler)(int64_t p, int64_t desloc, void *buf, int64_t n);      /* esp_err_t */
    int     (*particao_escrever)(int64_t p, int64_t desloc, const void *buf, int64_t n);
    int     (*particao_apagar)(int64_t p, int64_t desloc, int64_t n);
    int     (*flash_ler)(int64_t endereco, void *buf, int64_t n);

    /* ---- numeros ---- */
    double  (*mat)(int qual, double a, double b);                  /* CS_MAT_* */
    int     (*real_texto)(double v, int tipo, int precisao, int alternativo, char *buf, int n);
    double  (*texto_real)(const char *s, int *consumidos);

    /* ---- audio ---- */
    int64_t (*i2s_abrir)(int porta, int taxa, int bits, int canais, int bclk, int ws, int dout);
    int     (*i2s_escrever)(int64_t canal, const void *buf, int n, int64_t espera_us);  /* bytes; -1 parar */
    void    (*i2s_fechar)(int64_t canal);

    /* ---- WiFi e servidor HTTP ---- */
    int     (*wifi_conectar)(const char *ssid, const char *senha);
    int     (*wifi_estado)(void);                                  /* CS_WIFI_* */
    int     (*wifi_ip)(uint8_t ip[4], uint8_t mascara[4], uint8_t porta_de_saida[4]);
    void    (*wifi_desligar)(void);
    int     (*wifi_rssi)(void);
    void    (*wifi_mac)(uint8_t mac[6]);
    int64_t (*http_iniciar)(int porta);
    void    (*http_parar)(int64_t servidor);
    int64_t (*http_esperar)(int64_t servidor, int64_t espera_us, struct cs_pedido_http *info);
    int     (*http_status)(int64_t req, const char *status);
    int     (*http_tipo)(int64_t req, const char *tipo);
    int     (*http_cabecalho)(int64_t req, const char *nome, const char *valor);
    int     (*http_enviar)(int64_t req, const char *buf, int n);    /* 0 ok, -1 a conexao caiu */
    int     (*http_pedaco)(int64_t req, const char *buf, int n);    /* n == 0 fecha a resposta */
    int     (*http_consulta)(int64_t req, char *buf, int n);        /* tamanho, -1 sem consulta */
    int     (*http_ler_cabecalho)(int64_t req, const char *nome, char *buf, int n);
    int     (*http_ler_corpo)(int64_t req, char *buf, int n);
    void    (*http_fim)(int64_t req, int resultado);

    /* ---- a placa ---- */
    void    (*panico)(const char *motivo, const char *detalhe);
    void    (*reiniciar)(void);
    int64_t (*psram_tamanho)(void);
    int     (*chip)(char *buf, int n);
    uint32_t (*aleatorio)(void);
    int     (*nvs_gravar)(const char *espaco, const char *chave, int tipo, const void *dados, int n);
    int     (*nvs_ler)(const char *espaco, const char *chave, int *tipo, void *dados, int n);
    int     (*nvs_apagar)(const char *espaco, const char *chave);
    int     (*razao_do_reinicio)(void);                            /* esp_reset_reason_t */

    /* ---- USB (host) ---- */
    int     (*usb_pendrive)(void);                                 /* 1 = ha um pendrive ligado nos D-/D+ da placa */

    /* ---- a thread (direto do Windows: sem passar pelo C#) ---- */
    void   *(*tls_ler)(uint32_t indice);
} cs_host;

extern const cs_host *cs;
extern volatile int cs_parando;
extern uint32_t cs_tls;

/* Modos de pino (bits) */
#define CS_GPIO_ENTRADA   1
#define CS_GPIO_SAIDA     2
#define CS_GPIO_PULLUP    4
#define CS_GPIO_PULLDOWN  8
#define CS_GPIO_DRENO     16

/* Interrupcao de pino */
#define CS_BORDA_NENHUMA  0
#define CS_BORDA_SUBIDA   1
#define CS_BORDA_DESCIDA  2
#define CS_BORDA_AMBAS    3
#define CS_NIVEL_BAIXO    4
#define CS_NIVEL_ALTO     5

/* Semaforos */
#define CS_SEM_CONTAGEM   0
#define CS_SEM_MUTEX      1
#define CS_SEM_RECURSIVO  2

/* Filas */
#define CS_FILA_TRAS      0
#define CS_FILA_FRENTE    1
#define CS_FILA_SOBRESCREVER 2

/* Abrir arquivo (bits) */
#define CS_ARQ_LER        1
#define CS_ARQ_ESCREVER   2
#define CS_ARQ_CRIAR      4
#define CS_ARQ_TRUNCAR    8
#define CS_ARQ_ANEXAR     16
#define CS_ARQ_EXCLUSIVO  32

/* Montagem */
#define CS_MONTAR_SD_SPI  1
#define CS_MONTAR_SD_MMC  2
#define CS_MONTAR_FLASH   3
#define CS_MONTAR_FLASH_SO_LEITURA 4   /* esp_vfs_fat_spiflash_mount_ro */
#define CS_MONTAR_USB     5   /* msc_host_vfs_register: o pendrive */

/* WiFi */
#define CS_WIFI_DESLIGADO 0
#define CS_WIFI_CONECTANDO 1
#define CS_WIFI_CONECTADO 2
#define CS_WIFI_SEM_REDE  3
#define CS_WIFI_SENHA_ERRADA 4

/* Funcoes de matematica */
enum {
    CS_MAT_SIN, CS_MAT_COS, CS_MAT_TAN, CS_MAT_ASIN, CS_MAT_ACOS, CS_MAT_ATAN, CS_MAT_ATAN2,
    CS_MAT_SINH, CS_MAT_COSH, CS_MAT_TANH, CS_MAT_EXP, CS_MAT_LOG, CS_MAT_LOG10, CS_MAT_POW,
    CS_MAT_SQRT, CS_MAT_CBRT, CS_MAT_EXP2, CS_MAT_LOG2, CS_MAT_HYPOT, CS_MAT_EXPM1, CS_MAT_LOG1P,
    CS_MAT_ASINH, CS_MAT_ACOSH, CS_MAT_ATANH, CS_MAT_FMOD, CS_MAT_REMAINDER, CS_MAT_ERF, CS_MAT_ERFC,
    CS_MAT_TGAMMA, CS_MAT_LGAMMA
};

/* O que o servidor HTTP entrega para o tratador. */
typedef struct cs_pedido_http {
    int metodo;
    int64_t tamanho_do_corpo;
    char uri[520];
} cs_pedido_http;

/* Uma tarefa: a thread do PC que faz o papel de uma tarefa do FreeRTOS.
 * O salto e para onde a tarefa volta quando a placa para, quando alguem a
 * apaga -- ou quando o codigo dela da uma falha (ponteiro invalido,
 * divisao por zero): ai ela volta com o valor 2 e o simulador mostra o
 * "Guru Meditation" que a placa mostraria. */
typedef struct cs_tarefa {
    unsigned long long salto[32];
    int salto_pronto;
    void (*funcao)(void *);
    void *argumento;
    char nome[24];
    int prioridade;
    int nucleo;
    int pilha;
    int64_t aviso;              /* semaforo das notificacoes da tarefa */
    volatile uint32_t notificacao;
    volatile int notificada;
    volatile int apagar;
    volatile int suspender;
    int64_t retomar;            /* semaforo do vTaskResume */
    int em_isr;
    int estado;
    int id;
    int erro_c;                 /* o errno da tarefa */
    void *reserva;              /* a pilha dela, descontada da RAM interna como na placa */
    uint64_t falha_codigo;
    uint64_t falha_tipo;
    uint64_t falha_endereco;
    uint64_t falha_onde;
    struct cs_tarefa *proxima;
} cs_tarefa;

cs_tarefa *cs_tarefa_atual(void);
void cs_encerrar_tarefa(void) __attribute__((noreturn));
void cs_conferir_parada(void);
void cs_panico(const char *motivo, const char *detalhe) __attribute__((noreturn));

/* Depois de toda chamada ao simulador: a placa esta parando? */
#define CS_CONFERIR() do { if (cs_parando) cs_encerrar_tarefa(); } while (0)

#endif
