/* modnetwork.h -- o que o extmod/modnetwork.c ve da rede da placa. */
#ifndef CS_PY_MODNETWORK_H
#define CS_PY_MODNETWORK_H

#include "py/obj.h"

extern const mp_obj_type_t cs_wlan_type;
MP_DECLARE_CONST_FUN_OBJ_VAR_BETWEEN(cs_rede_ipconfig_obj);
MP_DECLARE_CONST_FUN_OBJ_VAR_BETWEEN(cs_rede_modo_obj);

#endif
