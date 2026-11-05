/* modtime.c -- o relogio do time: o da placa (incluido por extmod/modtime.c). */
#include "py/obj.h"
#include "shared/timeutils/timeutils.h"
#include "port/cs_py_ponte.h"

static void mp_time_localtime_get(timeutils_struct_time_t *tm)
{
    timeutils_seconds_since_epoch_to_struct_time(cs_py_epoca_us() / 1000000, tm);
}

static mp_obj_t mp_time_time_get(void) { return timeutils_obj_from_timestamp(cs_py_epoca_us() / 1000000); }
