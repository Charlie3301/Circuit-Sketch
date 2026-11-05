/* esp_cpu.h -- o do ESP-IDF. O computador nao e nem Xtensa nem RISC-V: o que ele le do processador
 * (o nucleo, os ciclos) vem daqui, com os nomes do caminho do RISC-V. */
#ifndef CS_ESP_CPU_H
#define CS_ESP_CPU_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif
int64_t esp_timer_get_time(void);
#ifdef __cplusplus
}
#endif

#ifndef __XTENSA__
static inline uint32_t rv_utils_get_core_id(void) { return 0; }
static inline void *rv_utils_get_sp(void) { return __builtin_frame_address(0); }
static inline uint32_t rv_utils_get_cycle_count(void) { return (uint32_t)(esp_timer_get_time() * 240); }
static inline void rv_utils_set_cycle_count(uint32_t c) { (void)c; }
static inline void rv_utils_set_mtvt(uint32_t v) { (void)v; }
static inline void rv_utils_set_xtvec(uint32_t v) { (void)v; }
static inline void rv_utils_intr_enable(uint32_t m) { (void)m; }
static inline void rv_utils_intr_disable(uint32_t m) { (void)m; }
static inline uint32_t rv_utils_intr_get_enabled_mask(void) { return 0; }
static inline void rv_utils_intr_edge_ack(uint32_t n) { (void)n; }
static inline void rv_utils_dbgr_break(void) { }
static inline bool rv_utils_dbgr_is_attached(void) { return false; }
static inline void rv_utils_en_branch_predictor(void) { }
static inline void rv_utils_dis_branch_predictor(void) { }
static inline void rv_utils_wfe_mode_enable(bool e) { (void)e; }
static inline void rv_utils_disable_wfe_mode(void) { }
#endif

typedef void (*intr_handler_t)(void *arg);
static inline void intr_handler_set(int n, intr_handler_t f, void *a) { (void)n; (void)f; (void)a; }
static inline intr_handler_t intr_handler_get(int n) { (void)n; return (intr_handler_t)0; }
static inline void *intr_handler_get_arg(int n) { (void)n; return (void *)0; }
#define CSR_PRV_MODE 0
#define RV_READ_CSR(c) 0
#define RV_WRITE_CSR(c, v) ((void)(v))
#define RV_SET_CSR(c, v) 0u
#define RV_CLEAR_CSR(c, v) 0u
#define MSTATUS_MIE 8u

#include_next "esp_cpu.h"
#endif
