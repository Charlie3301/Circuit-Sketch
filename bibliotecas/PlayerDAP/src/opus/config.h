/* config.h -- o que o ./configure da libopus escreveria, para o PlayerDAP.
 *
 * Daqui vem so o DECODIFICADOR da libopus 1.5.2 (Xiph.Org, licenca BSD),
 * em ponto flutuante -- o ESP32-S3 tem FPU -- e sem as otimizacoes de x86,
 * ARM e MIPS. As fontes vieram de celt/, silk/ e src/ para esta pasta so,
 * e cada .c ganhou uma linha no comeco, que inclui este arquivo (antes, so
 * com HAVE_CONFIG_H), porque uma biblioteca do Arduino nao recebe -D.
 */
#ifndef OPUS_DAP_CONFIG_H
#define OPUS_DAP_CONFIG_H

#define OPUS_BUILD 1
/* Os vetores temporarios saem de uma "pilha" global de 120 KB, alocada uma
 * vez no heap (na PSRAM), e nao da pilha da tarefa do audio, que e pequena.
 * Nao serve para duas tarefas decodificando ao mesmo tempo -- e o player so
 * tem uma. */
#define NONTHREADSAFE_PSEUDOSTACK 1
/* o newlib da ESP32 (e o runtime do simulador) tem lrint e lrintf: o
 * arredondamento certo, e nao o corte do cast */
#define HAVE_LRINT 1
#define HAVE_LRINTF 1

#endif
