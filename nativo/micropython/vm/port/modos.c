/* modos.c -- o os.urandom da placa (incluido por extmod/modos.c). */
#include "port/cs_py_ponte.h"

static mp_obj_t mp_os_urandom(mp_obj_t num)
{
    mp_int_t n = mp_obj_get_int(num);
    vstr_t vstr;
    vstr_init_len(&vstr, n);
    uint32_t r = 0;
    for (int i = 0; i < n; i++) {
        if ((i & 3) == 0) r = cs_py_aleatorio();
        vstr.buf[i] = r;
        r >>= 8;
    }
    return mp_obj_new_bytes_from_vstr(&vstr);
}
static MP_DEFINE_CONST_FUN_OBJ_1(mp_os_urandom_obj, mp_os_urandom);
