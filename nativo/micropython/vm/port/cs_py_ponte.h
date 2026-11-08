/* cs_py_ponte.h -- a placa vista do MicroPython (que e C): cada funcao daqui chama o core do Arduino
 * do simulador (src/port/cs_py_ponte.cpp), e dali em diante e o mesmo pino, o mesmo barramento e a
 * mesma rede do C++. */
#ifndef CS_PY_PONTE_H
#define CS_PY_PONTE_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

enum { CS_PY_ENTRADA, CS_PY_SAIDA, CS_PY_DRENO, CS_PY_PULL_UP, CS_PY_PULL_DOWN };
enum { CS_PY_SUBIDA = 1, CS_PY_DESCIDA = 2 };

uint32_t cs_py_ms(void);
uint32_t cs_py_us(void);
void cs_py_esperar_ms(uint32_t ms);
void cs_py_esperar_us(uint32_t us);
uint32_t cs_py_atomica_comeca(void);
void cs_py_atomica_termina(uint32_t estado);
uint32_t cs_py_aleatorio(void);
void cs_py_ceder(void);
void cs_py_reiniciar(void);
int cs_py_bootsel(void);
int64_t cs_py_epoca_us(void);
void cs_py_acertar_epoca_us(int64_t us);

/* O monitor serial */
void cs_py_serial_escrever(const char *s, size_t n);
int cs_py_serial_ler(void);

/* Os pinos */
void cs_py_pino_modo(int pino, int modo);
void cs_py_pino_escrever(int pino, int v);
int cs_py_pino_ler(int pino);
bool cs_py_pino_existe(int pino);
int cs_py_quantos_pinos(void);
typedef void (*cs_py_isr_t)(void *arg);
void cs_py_pino_interrupcao(int pino, int borda, cs_py_isr_t f, void *arg);
void cs_py_pino_sem_interrupcao(int pino);

/* ADC, PWM, DAC, toque */
int cs_py_adc_ler(int pino);
void cs_py_adc_bits(int bits);
void cs_py_adc_atenuacao(int pino, int atenuacao);
void cs_py_pwm_iniciar(int pino, uint32_t freq, uint32_t duty16);
void cs_py_pwm_duty(int pino, uint32_t duty16);
void cs_py_pwm_parar(int pino);
void cs_py_dac_escrever(int pino, int v);
int cs_py_toque_ler(int pino);

/* I2C e SPI de hardware */
void cs_py_i2c_iniciar(int bus, int sda, int scl, uint32_t freq);
void cs_py_i2c_parar(int bus);
int cs_py_i2c_escrever(int bus, uint16_t endereco, const uint8_t *dados, size_t n, bool parar);
int cs_py_i2c_ler(int bus, uint16_t endereco, uint8_t *dados, size_t n, bool parar);
void cs_py_spi_iniciar(int bus, int sck, int mosi, int miso, uint32_t freq, int polaridade, int fase, int primeiro);
void cs_py_spi_parar(int bus);
void cs_py_spi_trocar(int bus, size_t n, const uint8_t *saida, uint8_t *entrada);

/* UART */
void cs_py_uart_iniciar(int uart, uint32_t baud, int bits, int paridade, int parada, int tx, int rx, int rxbuf);
void cs_py_uart_parar(int uart);
size_t cs_py_uart_escrever(int uart, const uint8_t *dados, size_t n);
int cs_py_uart_disponivel(int uart);
int cs_py_uart_ler(int uart);
void cs_py_uart_quebra(int uart);

/* A fita e o DHT, que na bancada sao aparelhos: o protocolo de tempo nao passa pelo pino. */
void cs_py_fita_escrever(int pino, const uint8_t *bytes, size_t n);
int cs_py_dht_ler(int pino, uint8_t *cinco_bytes);

/* Timer (por cima do esp_timer) */
typedef void (*cs_py_timer_t)(void *arg);
void *cs_py_timer_novo(cs_py_timer_t f, void *arg);
void cs_py_timer_iniciar(void *t, uint64_t periodo_us, bool repete);
void cs_py_timer_parar(void *t);
void cs_py_timer_apagar(void *t);

/* Rede */
bool cs_py_wifi_ativar(int interface, bool ligado);
bool cs_py_wifi_ativo(int interface);
void cs_py_wifi_conectar(const char *ssid, const char *senha);
void cs_py_wifi_desconectar(void);
int cs_py_wifi_status(void);
bool cs_py_wifi_conectado(void);
void cs_py_wifi_criar_ap(const char *ssid, const char *senha);
int cs_py_wifi_clientes_do_ap(void);
void cs_py_wifi_ip(int interface, char ip[16], char mascara[16], char porta[16], char dns[16]);
void cs_py_wifi_definir_ip(int interface, const char *ip, const char *mascara, const char *porta, const char *dns);
void cs_py_wifi_mac(int interface, uint8_t mac[6]);
int cs_py_wifi_rssi(void);
int cs_py_wifi_varrer(void);
void cs_py_wifi_rede(int i, const char **ssid, uint8_t bssid[6], int *canal, int *rssi, int *seguranca);
const char *cs_py_wifi_nome(void);
void cs_py_wifi_definir_nome(const char *nome);

/* Sono */
void cs_py_dormir_fundo(uint64_t us);
void cs_py_dormir_leve(uint64_t us);
int cs_py_motivo_do_reinicio(void);

/* Os arquivos: a flash ("/") e o cartao ("/sd"). */
void *cs_py_arquivo_abrir(const char *caminho, const char *modo);
void cs_py_arquivo_fechar(void *a);
int cs_py_arquivo_ler(void *a, uint8_t *destino, size_t n);
int cs_py_arquivo_escrever(void *a, const uint8_t *origem, size_t n);
bool cs_py_arquivo_ir(void *a, int64_t posicao, int origem);
int64_t cs_py_arquivo_posicao(void *a);
void cs_py_arquivo_salvar(void *a);
bool cs_py_arq_existe(const char *caminho);
bool cs_py_arq_eh_pasta(const char *caminho);
int64_t cs_py_arq_tamanho(const char *caminho);
bool cs_py_arq_apagar(const char *caminho);
bool cs_py_arq_renomear(const char *de, const char *para);
bool cs_py_arq_criar_pasta(const char *caminho);
bool cs_py_arq_apagar_pasta(const char *caminho);
void *cs_py_pasta_abrir(const char *caminho);
bool cs_py_pasta_proximo(void *p, char *nome, size_t max, bool *pasta, int64_t *tamanho);
void cs_py_pasta_fechar(void *p);
bool cs_py_cartao_montar(void);
void cs_py_cartao_desmontar(void);

/* O erro que escapou do programa: o simulador mostra na linha certa. */
void cs_py_erro_do_programa(const char *tipo, const char *mensagem, const char *arquivo, int linha);

/* O programa e os .py das abas (gerados com o sketch, em cs_py_programa.cpp). */
typedef struct {
    const char *nome;
    const char *texto;
} cs_py_aba_t;
extern const cs_py_aba_t cs_py_abas[];
extern const int cs_py_quantas_abas;

/* Quem liga: roda o programa (a primeira aba, o main.py) e, se ele acabar, deixa o REPL no monitor. */
void cs_py_rodar(void);

#ifdef __cplusplus
}
#endif

#endif
