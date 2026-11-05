/* lwip/igmp.h -- os grupos de multicast: a rede do simulador nao separa por grupo. */
#ifndef CS_LWIP_IGMP_H
#define CS_LWIP_IGMP_H
#include "lwip/ip4_addr.h"
#include "lwip/err.h"
static inline err_t igmp_joingroup(const ip4_addr_t *ifaddr, const ip4_addr_t *groupaddr) { (void)ifaddr; (void)groupaddr; return ERR_OK; }
static inline err_t igmp_leavegroup(const ip4_addr_t *ifaddr, const ip4_addr_t *groupaddr) { (void)ifaddr; (void)groupaddr; return ERR_OK; }
#endif
